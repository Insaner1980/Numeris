using System;

namespace Numeris.Models;

public enum InsightSeverity
{
    Info,
    Warning,
    Critical,
}

public enum TrendState
{
    NoData,
    Flat,
    Up,
    Down,
    NewActivity,
}

public sealed record MetricWindow(double Current, double Previous);

public sealed record InsightCard(
    string Title,
    string Message,
    string WhyShown,
    string NextStep,
    InsightSeverity Severity,
    int Priority);

public sealed record StatusCodeSummary(
    long TotalRequests,
    long ClientErrorResponses,
    long ServerErrorResponses)
{
    public double ClientErrorShare => Ratio(ClientErrorResponses, TotalRequests);
    public double ServerErrorShare => Ratio(ServerErrorResponses, TotalRequests);

    private static double Ratio(long part, long total)
        => total > 0 ? (double)part / total : 0.0;
}

public sealed record IndexingSummary(
    int ActiveSitemapUrls,
    int InspectedUrls,
    int IndexedUrls)
{
    public int NotIndexedUrls => Math.Max(0, InspectedUrls - IndexedUrls);
    public double NotIndexedRatio => InspectedUrls > 0 ? (double)NotIndexedUrls / InspectedUrls : 0.0;
}

public sealed record SourceFreshnessSummary(
    int ConnectedSources,
    int MissingLastSyncSources,
    int StaleSources)
{
    public bool HasConnectedSources => ConnectedSources > 0;
    public bool HasStaleData => MissingLastSyncSources > 0 || StaleSources > 0;
}

public sealed record SearchPageTrend(
    string Page,
    long CurrentClicks,
    long PreviousClicks)
{
    public long ClickDelta => CurrentClicks - PreviousClicks;
}

public sealed record InsightMetrics(
    MetricWindow CloudflareVisitors,
    MetricWindow Ga4Users,
    MetricWindow GoogleImpressions,
    MetricWindow GoogleClicks,
    MetricWindow GoogleMobileClicks,
    MetricWindow GoogleMobileImpressions,
    MetricWindow PageSpeedMobileScore,
    MetricWindow BingImpressions,
    MetricWindow BingClicks,
    MetricWindow Ga4EngagementRate,
    MetricWindow Ga4KeyEvents,
    MetricWindow CloudflareCacheHitRatio,
    MetricWindow CloudflareThreats,
    StatusCodeSummary HttpStatus,
    IndexingSummary Indexing,
    SourceFreshnessSummary Freshness,
    SearchPageTrend? WorstDecliningPage,
    int NewSearchQueryCount,
    bool IsSingleDomain);

