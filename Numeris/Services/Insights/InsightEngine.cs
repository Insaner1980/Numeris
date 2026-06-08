using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Numeris.Models;

namespace Numeris.Services.Insights;

public sealed class InsightEngine
{
    private const int MaxInsightRows = 4;
    private const double TrafficIncreaseThreshold = 0.25;
    private const double FlatTrafficTolerance = 0.05;
    private const double GoogleImpressionIncreaseThreshold = 0.25;
    private const double DecliningPageRatioThreshold = 0.25;
    private const long DecliningPageClickThreshold = 5;
    private const double MobileDemandIncreaseThreshold = 0.20;
    private const double PageSpeedDropThreshold = 0.10;
    private const double WeakMobileScoreThreshold = 0.70;
    private const int IndexingIssueCountThreshold = 3;
    private const double IndexingIssueRatioThreshold = 0.20;
    private const int IndexingMissingActiveUrlThreshold = 5;
    private const long HttpRequestMinimum = 100;
    private const double ServerErrorShareThreshold = 0.01;
    private const long ServerErrorCountThreshold = 10;
    private const double ClientErrorShareThreshold = 0.05;
    private const long BingImpressionMinimum = 50;
    private const double EngagementTrafficIncreaseThreshold = 0.25;
    private const double EngagementRateDropThreshold = 0.10;
    private const double CacheHitDropThreshold = 0.15;
    private const double WeakCacheHitRatioThreshold = 0.50;
    private const double ThreatIncreaseThreshold = 0.50;
    private const long ThreatMinimum = 10;

    public IReadOnlyList<InsightCard> Generate(InsightMetrics metrics)
    {
        var cards = new List<InsightCard>();

        AddIfPresent(cards, DataStale(metrics));
        AddIfPresent(cards, TrafficMismatch(metrics));
        AddIfPresent(cards, GoogleVisibilityWithoutClicks(metrics));
        AddIfPresent(cards, DecliningPage(metrics));
        AddIfPresent(cards, NewSearchQueries(metrics));
        AddIfPresent(cards, MobileDemandRisk(metrics));
        AddIfPresent(cards, IndexingIssue(metrics));
        AddIfPresent(cards, IndexingDataMissing(metrics));
        AddIfPresent(cards, HttpErrors(metrics));
        AddIfPresent(cards, BingVisibilityWithoutClicks(metrics));
        AddIfPresent(cards, EngagementGap(metrics));
        AddIfPresent(cards, CacheEfficiency(metrics));
        AddIfPresent(cards, ThreatSpike(metrics));

        return cards
            .OrderByDescending(card => card.Severity)
            .ThenBy(card => card.Priority)
            .Take(MaxInsightRows)
            .ToList();
    }

    public string GetEmptyStateText(InsightMetrics metrics)
    {
        return metrics.Freshness.HasConnectedSources && HasAnyData(metrics)
            ? "All connected sources look consistent for this period."
            : "Connect or refresh sources to generate insights.";
    }

    private static InsightCard? DataStale(InsightMetrics metrics)
    {
        if (!metrics.Freshness.HasConnectedSources || !metrics.Freshness.HasStaleData)
        {
            return null;
        }

        return new InsightCard(
            "Some data may be out of date",
            "One or more connected sources have not synced recently, so the insights may not reflect the latest traffic.",
            $"{metrics.Freshness.MissingLastSyncSources} connected sources have no sync time and {metrics.Freshness.StaleSources} last synced more than 48 hours ago.",
            "Open Sources and check connected source status.",
            InsightSeverity.Warning,
            10);
    }

    private static InsightCard? TrafficMismatch(InsightMetrics metrics)
    {
        if (!HasWindow(metrics.CloudflareVisitors)
            || !HasWindow(metrics.Ga4Users)
            || metrics.CloudflareVisitors.Current < 50
            || !IsUp(metrics.CloudflareVisitors, TrafficIncreaseThreshold)
            || !IsFlat(metrics.Ga4Users, FlatTrafficTolerance))
        {
            return null;
        }

        return new InsightCard(
            "Traffic looks inconsistent",
            "Cloudflare recorded more visitors, but Analytics users stayed almost the same. This can happen with bots, cached or static requests, or tracking gaps.",
            $"Cloudflare visitors increased by {PositiveRatio(metrics.CloudflareVisitors)}, while Analytics users changed by {ChangedRatio(metrics.Ga4Users)}.",
            "Open Cloudflare and review Traffic, Cache, or Status Codes.",
            InsightSeverity.Warning,
            30);
    }

    private static InsightCard? GoogleVisibilityWithoutClicks(InsightMetrics metrics)
    {
        if (!HasWindow(metrics.GoogleImpressions)
            || !HasWindow(metrics.GoogleClicks)
            || metrics.GoogleImpressions.Current < 100
            || !IsUp(metrics.GoogleImpressions, GoogleImpressionIncreaseThreshold)
            || !IsFlat(metrics.GoogleClicks, FlatTrafficTolerance))
        {
            return null;
        }

        return new InsightCard(
            "People see your pages, but do not click",
            "Google impressions are rising, but clicks are not. Your result may need a better title, snippet, or ranking position.",
            $"Google impressions increased by {PositiveRatio(metrics.GoogleImpressions)}, while clicks changed by {ChangedRatio(metrics.GoogleClicks)}.",
            "Open Google Search and review Queries, Pages, and Indexing.",
            InsightSeverity.Warning,
            40);
    }

    private static InsightCard? DecliningPage(InsightMetrics metrics)
    {
        var page = metrics.WorstDecliningPage;
        if (page is null || page.PreviousClicks < DecliningPageClickThreshold || page.CurrentClicks >= page.PreviousClicks)
        {
            return null;
        }

        var lostClicks = page.PreviousClicks - page.CurrentClicks;
        var lostEnough = lostClicks >= DecliningPageClickThreshold
            || ((double)lostClicks / page.PreviousClicks) >= DecliningPageRatioThreshold;
        if (!lostEnough)
        {
            return null;
        }

        return new InsightCard(
            "A previously useful page is losing search traffic",
            "One page that used to bring Google clicks is now bringing fewer clicks.",
            $"{ShortPage(page.Page)} changed from {page.PreviousClicks.ToString(CultureInfo.InvariantCulture)} to {page.CurrentClicks.ToString(CultureInfo.InvariantCulture)} Google clicks.",
            "Open Google Search and review Queries, Pages, and Indexing.",
            InsightSeverity.Warning,
            45);
    }

    private static InsightCard? NewSearchQueries(InsightMetrics metrics)
    {
        if (!metrics.IsSingleDomain || metrics.NewSearchQueryCount <= 0)
        {
            return null;
        }

        return new InsightCard(
            "New search queries are bringing traffic",
            "Google is sending clicks from queries that were not visible in the previous period.",
            $"{metrics.NewSearchQueryCount.ToString(CultureInfo.InvariantCulture)} new queries brought Google clicks in this period.",
            "Open Google Search and review Queries, Pages, and Indexing.",
            InsightSeverity.Info,
            120);
    }

    private static InsightCard? MobileDemandRisk(InsightMetrics metrics)
    {
        var mobileDemandGrowing = IsUp(metrics.GoogleMobileClicks, MobileDemandIncreaseThreshold)
            || IsUp(metrics.GoogleMobileImpressions, MobileDemandIncreaseThreshold);
        var mobilePerformanceWeak = IsDown(metrics.PageSpeedMobileScore, PageSpeedDropThreshold)
            || (metrics.PageSpeedMobileScore.Current > 0 && metrics.PageSpeedMobileScore.Current < WeakMobileScoreThreshold);

        if (!mobileDemandGrowing || !mobilePerformanceWeak)
        {
            return null;
        }

        return new InsightCard(
            "Mobile traffic may need attention",
            "Mobile search demand is growing while mobile performance looks weak or is getting worse.",
            $"Mobile impressions changed by {ChangedRatio(metrics.GoogleMobileImpressions)}, mobile clicks changed by {ChangedRatio(metrics.GoogleMobileClicks)}, and mobile score is {Score(metrics.PageSpeedMobileScore.Current)}.",
            "Open Performance and review PageSpeed and Core Web Vitals.",
            InsightSeverity.Warning,
            50);
    }

    private static InsightCard? IndexingIssue(InsightMetrics metrics)
    {
        var indexing = metrics.Indexing;
        if (indexing.ActiveSitemapUrls <= 0 || indexing.InspectedUrls <= 0)
        {
            return null;
        }

        if (indexing.NotIndexedUrls < IndexingIssueCountThreshold
            && indexing.NotIndexedRatio < IndexingIssueRatioThreshold)
        {
            return null;
        }

        return new InsightCard(
            "Some sitemap pages may not be indexed",
            "Numeris found sitemap URLs that do not currently have a passing Google URL Inspection result.",
            $"{indexing.IndexedUrls.ToString(CultureInfo.InvariantCulture)} of {indexing.InspectedUrls.ToString(CultureInfo.InvariantCulture)} inspected sitemap URLs are indexed.",
            "Open Google Search and review Queries, Pages, and Indexing.",
            InsightSeverity.Warning,
            25);
    }

    private static InsightCard? IndexingDataMissing(InsightMetrics metrics)
    {
        var indexing = metrics.Indexing;
        if (indexing.ActiveSitemapUrls < IndexingMissingActiveUrlThreshold || indexing.InspectedUrls != 0)
        {
            return null;
        }

        return new InsightCard(
            "Indexing data is missing",
            "Numeris knows about sitemap URLs, but they have not been inspected through Google URL Inspection yet.",
            $"{indexing.ActiveSitemapUrls.ToString(CultureInfo.InvariantCulture)} active sitemap URLs have no inspection result yet.",
            "Open Google Search and review Queries, Pages, and Indexing.",
            InsightSeverity.Info,
            90);
    }

    private static InsightCard? HttpErrors(InsightMetrics metrics)
    {
        var status = metrics.HttpStatus;
        if (status.TotalRequests < HttpRequestMinimum)
        {
            return null;
        }

        if (status.ServerErrorShare >= ServerErrorShareThreshold
            || status.ServerErrorResponses >= ServerErrorCountThreshold)
        {
            return new InsightCard(
                "Some visitors may be hitting errors",
                "Cloudflare recorded HTTP error responses during this period.",
                $"Server error responses were {Percent(status.ServerErrorShare)} of Cloudflare requests.",
                "Open Cloudflare and review Traffic, Cache, or Status Codes.",
                InsightSeverity.Critical,
                5);
        }

        if (status.ClientErrorShare >= ClientErrorShareThreshold)
        {
            return new InsightCard(
                "Some visitors may be hitting errors",
                "Cloudflare recorded HTTP error responses during this period.",
                $"Client error responses were {Percent(status.ClientErrorShare)} of Cloudflare requests.",
                "Open Cloudflare and review Traffic, Cache, or Status Codes.",
                InsightSeverity.Warning,
                55);
        }

        return null;
    }

    private static InsightCard? BingVisibilityWithoutClicks(InsightMetrics metrics)
    {
        if (metrics.BingImpressions.Current < BingImpressionMinimum || metrics.BingClicks.Current != 0)
        {
            return null;
        }

        return new InsightCard(
            "Bing sees the site, but brings no visitors",
            "Bing has impressions for the site, but no clicks in the selected period.",
            $"Bing recorded {Number(metrics.BingImpressions.Current)} impressions and {Number(metrics.BingClicks.Current)} clicks.",
            "Open Bing and review Queries, Pages, and Crawl.",
            InsightSeverity.Info,
            130);
    }

    private static InsightCard? EngagementGap(InsightMetrics metrics)
    {
        var trafficGrowing = IsUp(metrics.Ga4Users, EngagementTrafficIncreaseThreshold);
        var engagementDown = HasWindow(metrics.Ga4EngagementRate)
            && IsDown(metrics.Ga4EngagementRate, EngagementRateDropThreshold);
        var keyEventsNotImproving = HasWindow(metrics.Ga4KeyEvents)
            && (IsDown(metrics.Ga4KeyEvents) || IsFlat(metrics.Ga4KeyEvents, FlatTrafficTolerance));

        if (!trafficGrowing || (!engagementDown && !keyEventsNotImproving))
        {
            return null;
        }

        return new InsightCard(
            "More visitors are not becoming more engaged",
            "Analytics traffic is growing, but engagement or key events are not improving with it.",
            $"Analytics users changed by {ChangedRatio(metrics.Ga4Users)}, engagement changed by {ChangedRatio(metrics.Ga4EngagementRate)}, and key events changed by {ChangedRatio(metrics.Ga4KeyEvents)}.",
            "Open Analytics and review Pages, Acquisition, Events, and Devices.",
            InsightSeverity.Warning,
            60);
    }

    private static InsightCard? CacheEfficiency(InsightMetrics metrics)
    {
        if (metrics.HttpStatus.TotalRequests < HttpRequestMinimum
            || metrics.CloudflareCacheHitRatio.Current >= WeakCacheHitRatioThreshold
            || !IsDown(metrics.CloudflareCacheHitRatio, CacheHitDropThreshold))
        {
            return null;
        }

        return new InsightCard(
            "Caching may be less effective",
            "Cloudflare is serving fewer requests from cache than before.",
            $"Cache hit ratio changed by {ChangedRatio(metrics.CloudflareCacheHitRatio)} and is now {Percent(metrics.CloudflareCacheHitRatio.Current)}.",
            "Open Cloudflare and review Traffic, Cache, or Status Codes.",
            InsightSeverity.Warning,
            70);
    }

    private static InsightCard? ThreatSpike(InsightMetrics metrics)
    {
        var state = Trend.Classify(metrics.CloudflareThreats, upThreshold: ThreatIncreaseThreshold);
        if (metrics.CloudflareThreats.Current < ThreatMinimum
            || (state != TrendState.NewActivity && state != TrendState.Up))
        {
            return null;
        }

        return new InsightCard(
            "Cloudflare is blocking more suspicious traffic",
            "Threat events increased during this period.",
            $"Cloudflare threat events changed from {Number(metrics.CloudflareThreats.Previous)} to {Number(metrics.CloudflareThreats.Current)}.",
            "Open Cloudflare and review Traffic, Cache, or Status Codes.",
            InsightSeverity.Warning,
            20);
    }

    private static bool HasAnyData(InsightMetrics metrics)
    {
        return new[]
            {
                metrics.CloudflareVisitors,
                metrics.Ga4Users,
                metrics.GoogleImpressions,
                metrics.GoogleClicks,
                metrics.GoogleMobileClicks,
                metrics.GoogleMobileImpressions,
                metrics.PageSpeedMobileScore,
                metrics.BingImpressions,
                metrics.BingClicks,
                metrics.Ga4EngagementRate,
                metrics.Ga4KeyEvents,
                metrics.CloudflareCacheHitRatio,
                metrics.CloudflareThreats,
            }
            .Any(HasWindow)
            || metrics.HttpStatus.TotalRequests > 0
            || metrics.Indexing.ActiveSitemapUrls > 0;
    }

    private static void AddIfPresent(ICollection<InsightCard> cards, InsightCard? card)
    {
        if (card is not null)
        {
            cards.Add(card);
        }
    }

    private static bool HasWindow(MetricWindow metric)
        => metric.Current > 0.0 || metric.Previous > 0.0;

    private static bool IsUp(MetricWindow metric, double threshold)
        => HasWindow(metric) && Trend.Classify(metric, upThreshold: threshold) == TrendState.Up;

    private static bool IsFlat(MetricWindow metric, double tolerance)
        => HasWindow(metric) && Trend.IsFlat(metric, tolerance);

    private static bool IsDown(MetricWindow metric, double threshold = 0.10)
        => HasWindow(metric) && Trend.Classify(metric, downThreshold: threshold) == TrendState.Down;

    private static string PositiveRatio(MetricWindow metric)
        => Trend.FormatRatio(Trend.ChangeRatio(metric)).TrimStart('+');

    private static string ChangedRatio(MetricWindow metric)
        => metric.Previous == 0.0 && metric.Current > 0.0
            ? "new activity"
            : Trend.FormatRatio(Trend.ChangeRatio(metric));

    private static string Percent(double ratio)
        => (ratio * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Score(double score)
        => score <= 0 ? "not available" : (score * 100.0).ToString("0", CultureInfo.InvariantCulture);

    private static string Number(double value)
        => value.ToString("0", CultureInfo.InvariantCulture);

    private static string ShortPage(string page)
    {
        if (page.Length <= 80)
        {
            return page;
        }

        return page[..77] + "...";
    }
}
