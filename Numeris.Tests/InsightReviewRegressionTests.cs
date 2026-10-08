using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Dapper;
using Numeris.Models;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Insights;

internal static class InsightReviewRegressionTests
{
    public static void FormatsLargeGrowthWithoutReversingItsSign()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            Require(Trend.FormatRatio(30_000_000) == "+3000000000%", "A large real ratio must not overflow into a decline");
            Require(Trend.FormatRatio(0.004) == "0%" && Trend.FormatRatio(-0.005) == "-1%"
                && Trend.FormatRatio(0.34) == "+34%", "Rounding and signed ordinary ratios must remain unchanged");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    public static void IncludesPerDomainCloudflareFreshness()
    {
        using var database = CreateDatabase();
        var fresh = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var stale = DateTime.Now.AddHours(-49).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO connections(id,source,status,config,last_sync) VALUES
            ('cloudflare:stale.example','cloudflare','connected','{}',@stale),
            ('cloudflare:fresh.example','cloudflare','connected','{}',@fresh),
            ('cloudflare:missing.example','cloudflare','configured','{}',NULL),
            ('cloudflare:off.example','cloudflare','disconnected','{}',@stale);
            """, new { fresh, stale })).GetAwaiter().GetResult();
        var metrics = new InsightMetricsRepository(database).GetInsightMetricsAsync("all", 7).GetAwaiter().GetResult();
        Require(metrics.Freshness.ConnectedSources == 3 && metrics.Freshness.StaleSources == 1
            && metrics.Freshness.MissingLastSyncSources == 1, "Active per-domain source rows must not be hidden by the disconnected placeholder");
    }

    public static void EmptyStateDoesNotCertifyAllConnectedSources()
    {
        using var database = CreateDatabase();
        var metrics = new InsightMetricsRepository(database).GetInsightMetricsAsync("all", 7).GetAwaiter().GetResult() with
        {
            CloudflareVisitors = new MetricWindow(100, 100),
            Freshness = new SourceFreshnessSummary(2, 0, 0),
        };
        Require(InsightEngine.Generate(metrics).Count == 0, "The fixture must trigger no rule while other source data is missing");
        Require(InsightEngine.GetEmptyStateText(metrics) == "No insight rules were triggered by the stored data for this period.",
            "No rule matches must describe the evaluated data rather than certify every source");
    }

    public static void IncludesExactMobileScoreDropBoundary()
    {
        using var database = CreateDatabase();
        var metrics = new InsightMetricsRepository(database).GetInsightMetricsAsync("all", 7).GetAwaiter().GetResult() with
        {
            GoogleMobileClicks = new MetricWindow(120, 100),
            PageSpeedMobileScore = new MetricWindow(0.81, 0.90),
        };
        Require(InsightEngine.Generate(metrics).Any(card => card.Title == "Mobile traffic may need attention"),
            "The exact ten-percent mobile score decline must qualify despite binary floating-point rounding");
        Require(!InsightEngine.Generate(metrics with { PageSpeedMobileScore = new MetricWindow(0.82, 0.90) })
            .Any(card => card.Title == "Mobile traffic may need attention"), "A smaller decline with a healthy score must not qualify");
        Require(Trend.IsUp(new MetricWindow(0.30, 0.24)) && Trend.IsFlat(new MetricWindow(0.84, 0.80))
            && !Trend.IsFlat(new MetricWindow(0.841, 0.80)), "Inclusive growth/flat boundaries and their neighbors must agree");
    }

    public static void VerifiesRuleThresholdsAndOrdering()
    {
        using var database = CreateDatabase();
        var baseline = new InsightMetricsRepository(database).GetInsightMetricsAsync("all", 7).GetAwaiter().GetResult();
        var visibility = baseline with { GoogleImpressions = new(100, 80), GoogleClicks = new(105, 100) };
        var mobile = baseline with { GoogleMobileImpressions = new(120, 100), PageSpeedMobileScore = new(0.69, 0) };
        var requests = baseline with { HttpStatus = new(100, 0, 0) };
        var cases = new (string Name, InsightMetrics Metrics, int? Priority)[]
        {
            ("no data", baseline, null),
            ("missing freshness", baseline with { Freshness = new(1, 1, 0) }, 10),
            ("disconnected freshness", baseline with { Freshness = new(0, 0, 1) }, null),
            ("Google exact growth and flat limit", visibility, 40),
            ("Google below impressions", visibility with { GoogleImpressions = new(99, 79) }, null),
            ("Google below growth", visibility with { GoogleImpressions = new(100, 81) }, null),
            ("Google beyond flat tolerance", visibility with { GoogleClicks = new(106, 100) }, null),
            ("Google absent clicks", visibility with { GoogleClicks = new(0, 0) }, null),
            ("page absolute loss", baseline with { WorstDecliningPage = new("/absolute", 95, 100) }, 45),
            ("page relative loss", baseline with { WorstDecliningPage = new("/relative", 6, 8) }, 45),
            ("page insufficient previous clicks", baseline with { WorstDecliningPage = new("/small", 0, 4) }, null),
            ("page below both loss limits", baseline with { WorstDecliningPage = new("/steady", 7, 8) }, null),
            ("single-domain new query", baseline with { IsSingleDomain = true, NewSearchQueryCount = 1 }, 120),
            ("all-domain new query", baseline with { NewSearchQueryCount = 1 }, null),
            ("mobile impressions growth and weak score", mobile, 50),
            ("mobile score strict boundary", mobile with { PageSpeedMobileScore = new(0.70, 0) }, null),
            ("mobile missing score", mobile with { PageSpeedMobileScore = new(0, 0) }, null),
            ("mobile below demand", mobile with { GoogleMobileImpressions = new(119, 100) }, null),
            ("indexing relative boundary", baseline with { Indexing = new(10, 10, 8) }, 25),
            ("indexing absolute boundary", baseline with { Indexing = new(100, 100, 97) }, 25),
            ("indexing below both boundaries", baseline with { Indexing = new(100, 100, 98) }, null),
            ("missing indexing five", baseline with { Indexing = new(5, 0, 0) }, 90),
            ("missing indexing four", baseline with { Indexing = new(4, 0, 0) }, null),
            ("one inspected URL", baseline with { Indexing = new(5, 1, 1) }, null),
            ("HTTP minimum", baseline with { HttpStatus = new(99, 10, 10) }, null),
            ("HTTP client ratio", baseline with { HttpStatus = new(100, 5, 0) }, 55),
            ("HTTP server precedence", baseline with { HttpStatus = new(100, 5, 1) }, 5),
            ("HTTP server count", baseline with { HttpStatus = new(10000, 0, 10) }, 5),
            ("HTTP below thresholds", baseline with { HttpStatus = new(1000, 49, 9) }, null),
            ("Bing fifty impressions", baseline with { BingImpressions = new(50, 0), BingClicks = new(0, 0) }, 130),
            ("Bing forty-nine impressions", baseline with { BingImpressions = new(49, 0) }, null),
            ("Bing positive clicks", baseline with { BingImpressions = new(50, 0), BingClicks = new(1, 0) }, null),
            ("cache exact relative drop", requests with { CloudflareCacheHitRatio = new(0.425, 0.50) }, 70),
            ("cache strict current limit", requests with { CloudflareCacheHitRatio = new(0.50, 0.75) }, null),
            ("cache below relative drop", requests with { CloudflareCacheHitRatio = new(0.43, 0.50) }, null),
            ("cache absent baseline", requests with { CloudflareCacheHitRatio = new(0.425, 0) }, null),
            ("threat new activity", baseline with { CloudflareThreats = new(10, 0) }, 20),
            ("threat exact growth", baseline with { CloudflareThreats = new(15, 10) }, 20),
            ("threat below growth", baseline with { CloudflareThreats = new(14, 10) }, null),
            ("threat below count", baseline with { CloudflareThreats = new(9, 0) }, null),
        };
        foreach (var (name, metrics, priority) in cases)
        {
            var cards = InsightEngine.Generate(metrics);
            Require(priority.HasValue ? cards.Count == 1 && cards[0].Priority == priority : cards.Count == 0,
                $"Unexpected insight eligibility: {name}");
            if (cards.Count > 0)
                Require(!string.IsNullOrWhiteSpace(cards[0].WhyShown) && !string.IsNullOrWhiteSpace(cards[0].NextStep),
                    $"Triggered insight must contain evidence and an advisory next step: {name}");
        }
        var many = visibility with
        {
            Freshness = new(1, 0, 1),
            CloudflareThreats = new(10, 0),
            HttpStatus = new(100, 5, 1),
            Indexing = new(10, 10, 8),
            WorstDecliningPage = new("/losing", 0, 10),
        };
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var cards = InsightEngine.Generate(many);
            Require(cards.Select(card => card.Priority).SequenceEqual(InsightReviewRegressionTestsInputs.Vector1)
                && cards[0].Severity == InsightSeverity.Critical,
                "Critical severity must precede warnings; priorities and the four-card limit must be deterministic");
        }
    }

    public static void DistinguishesMissingThreatHistoryFromMeasuredZero()
    {
        using var database = CreateDatabase();
        var current = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var previous = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO cloudflare_traffic(domain,date,threats,fetched_at) VALUES ('test.example',@current,10,'now');
            """, new { current })).GetAwaiter().GetResult();
        var repository = new InsightMetricsRepository(database);
        var missing = repository.GetInsightMetricsAsync("test.example", 7).GetAwaiter().GetResult();
        Require(Trend.Classify(missing.CloudflareThreats) == TrendState.NoData
            && !InsightEngine.Generate(missing).Any(card => card.Priority == 20),
            "Missing history must not invent new threat activity");
        database.WriteAsync(c => c.Execute("""
            INSERT INTO cloudflare_traffic(domain,date,threats,fetched_at) VALUES ('test.example',@previous,0,'now');
            """, new { previous })).GetAwaiter().GetResult();
        var measured = repository.GetInsightMetricsAsync("test.example", 7).GetAwaiter().GetResult();
        Require(Trend.Classify(measured.CloudflareThreats) == TrendState.NewActivity
            && InsightEngine.Generate(measured).Any(card => card.Priority == 20),
            "A measured zero baseline must still allow new activity");
    }

    public static void DoesNotInventZeroBingClicks()
    {
        using var database = CreateDatabase();
        var current = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO bing_rank_traffic(site_url,date,impressions,clicks,raw_json,fetched_at)
            VALUES ('https://test.example/',@current,50,NULL,'{}','now');
            """, new { current })).GetAwaiter().GetResult();
        var repository = new InsightMetricsRepository(database);
        Require(!InsightEngine.Generate(repository.GetInsightMetricsAsync("test.example", 7).Result).Any(card => card.Priority == 130),
            "Unavailable Bing clicks must not be described as measured zero");
        database.WriteAsync(c => c.Execute("UPDATE bing_rank_traffic SET clicks=0")).GetAwaiter().GetResult();
        Require(InsightEngine.Generate(repository.GetInsightMetricsAsync("test.example", 7).Result).Any(card => card.Priority == 130),
            "Measured zero Bing clicks must still trigger the current-period rule");
    }

    public static void RequiresObservedPageAndQueryComparisonRows()
    {
        using var database = CreateDatabase();
        var current = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var previous = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_console(site_url,date,kind,query,page,clicks,fetched_at) VALUES
            ('test.example',@previous,'page','','https://test.example/old',10,'now'),
            ('test.example',@current,'query','new query',NULL,5,'now');
            """, new { current, previous })).GetAwaiter().GetResult();
        var repository = new InsightMetricsRepository(database);
        var missing = repository.GetInsightMetricsAsync("test.example", 7).Result;
        Require(missing.WorstDecliningPage is null && missing.NewSearchQueryCount == 0,
            "An absent page or query dataset must not create losses or new queries");
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_console(site_url,date,kind,query,page,clicks,fetched_at)
            VALUES ('test.example',@current,'page','','https://test.example/other',0,'now');
            """, new { current })).GetAwaiter().GetResult();
        Require(repository.GetInsightMetricsAsync("test.example", 7).Result.WorstDecliningPage is null,
            "Another page's current result must not invent zero clicks for an unobserved page");
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_console(site_url,date,kind,query,page,clicks,fetched_at) VALUES
            ('test.example',@current,'page','','https://test.example/old',0,'now'),
            ('test.example',@previous,'query','existing query',NULL,0,'now');
            """, new { current, previous })).GetAwaiter().GetResult();
        var measured = repository.GetInsightMetricsAsync("test.example", 7).Result;
        Require(measured.WorstDecliningPage?.CurrentClicks == 0 && measured.NewSearchQueryCount == 1,
            "Observed zero page clicks and an observed query baseline must retain their rules");
    }

    public static void KeepsMissingMobileEvidenceOutOfTheExplanation()
    {
        using var database = CreateDatabase();
        var current = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var previous = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO search_devices(site_url,date,device,clicks,impressions) VALUES
            ('test.example',@current,'mobile',120,120),('test.example',@previous,'mobile',100,100);
            INSERT INTO pagespeed_runs(url,strategy,analysis_utc,performance_score,raw_json,fetched_at)
            VALUES ('https://test.example/','MOBILE',@previous,0.9,'{}','now');
            """, new { current, previous })).GetAwaiter().GetResult();
        var repository = new InsightMetricsRepository(database);
        Require(!InsightEngine.Generate(repository.GetInsightMetricsAsync("test.example", 7).Result)
            .Any(card => card.Priority == 50), "A missing current score must not be a 100-percent performance decline");
        database.WriteAsync(c => c.Execute("""
            INSERT INTO pagespeed_runs(url,strategy,analysis_utc,performance_score,raw_json,fetched_at)
            VALUES ('https://test.example/','MOBILE',@current,0.6,'{}','now');
            DELETE FROM search_devices WHERE date=@current;
            """, new { current })).GetAwaiter().GetResult();
        Require(!InsightEngine.Generate(repository.GetInsightMetricsAsync("test.example", 7).Result)
            .Any(card => card.Priority == 50), "Missing current mobile demand must not trigger the rule either");
    }

    public static void PreservesMetricAvailabilityForAllAggregates()
    {
        using var database = CreateDatabase();
        var repository = new InsightMetricsRepository(database);
        var absent = repository.GetInsightMetricsAsync("test.example", 7).Result;
        Require(new[] { absent.CloudflareVisitors, absent.GoogleClicks, absent.GoogleMobileClicks,
            absent.PageSpeedMobileScore, absent.BingClicks, absent.CloudflareCacheHitRatio }
            .All(metric => !metric.HasCurrent && !metric.HasPrevious), "Empty tables must retain unavailable windows");
        var current = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO cloudflare_traffic(domain,date,requests,cached_requests,fetched_at) VALUES ('test.example',@current,100,0,'now');
            INSERT INTO search_console(site_url,date,kind,query,clicks,fetched_at) VALUES ('test.example',@current,'daily','',0,'now');
            INSERT INTO search_devices(site_url,date,device,clicks) VALUES ('test.example',@current,'mobile',0);
            INSERT INTO pagespeed_runs(url,strategy,analysis_utc,performance_score,raw_json,fetched_at)
            VALUES ('https://test.example/','MOBILE',@current,0,'{}','now');
            """, new { current })).GetAwaiter().GetResult();
        var measured = repository.GetInsightMetricsAsync("test.example", 7).Result;
        Require(new[] { measured.CloudflareVisitors, measured.GoogleClicks, measured.GoogleMobileClicks,
            measured.PageSpeedMobileScore, measured.CloudflareCacheHitRatio }
            .All(metric => metric.HasCurrent && metric.Current == 0 && !metric.HasPrevious),
            "Observed zeros must remain available while missing previous windows stay unavailable");
        database.WriteAsync(c => c.Execute("UPDATE cloudflare_traffic SET requests=0; UPDATE pagespeed_runs SET performance_score=NULL;"))
            .GetAwaiter().GetResult();
        var undefined = repository.GetInsightMetricsAsync("test.example", 7).Result;
        Require(!undefined.CloudflareCacheHitRatio.HasCurrent && !undefined.PageSpeedMobileScore.HasCurrent,
            "Undefined cache ratios and null Lighthouse scores must not become measured zeros");
    }

    public static void DoesNotCompareDisjointSiteHistories()
    {
        using var database = CreateDatabase();
        var current = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var previous = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO cloudflare_traffic(domain,date,threats,fetched_at) VALUES
            ('new.example',@current,10,'now'),('older.example',@previous,0,'now');
            INSERT INTO search_console(site_url,date,kind,query,impressions,clicks,fetched_at) VALUES
            ('new.example',@current,'daily','',150,10,'now'),('older.example',@previous,'daily','',100,10,'now');
            INSERT INTO search_devices(site_url,date,device,impressions,clicks) VALUES
            ('new.example',@current,'mobile',120,120),('older.example',@previous,'mobile',100,100);
            INSERT INTO pagespeed_runs(url,strategy,analysis_utc,performance_score,raw_json,fetched_at) VALUES
            ('https://new.example/','MOBILE',@current,0.8,'{}','now'),('https://older.example/','MOBILE',@previous,0.9,'{}','now');
            INSERT INTO bing_rank_traffic(site_url,date,impressions,clicks,raw_json,fetched_at) VALUES
            ('https://new.example/',@current,100,5,'{}','now'),('https://older.example/',@previous,100,5,'{}','now');
            """, new { current, previous })).GetAwaiter().GetResult();
        var repository = new InsightMetricsRepository(database);
        var missing = repository.GetInsightMetricsAsync("all", 7).Result;
        Require(new[] { missing.CloudflareVisitors, missing.CloudflareThreats, missing.GoogleClicks,
            missing.GoogleMobileClicks, missing.PageSpeedMobileScore, missing.BingClicks }
            .All(metric => !metric.HasComparison), "Every source must compare the same observed site or URL identities");
        Require(!InsightEngine.Generate(missing)
            .Any(card => card.Priority == 20), "Another site's history must not supply a missing threat baseline");
        database.WriteAsync(c => c.Execute("""
            INSERT INTO cloudflare_traffic(domain,date,threats,fetched_at) VALUES
            ('new.example',@previous,0,'now'),('older.example',@current,0,'now');
            """, new { current, previous })).GetAwaiter().GetResult();
        Require(InsightEngine.Generate(repository.GetInsightMetricsAsync("all", 7).Result)
            .Any(card => card.Priority == 20), "Paired measured-zero histories must retain the aggregate rule");
    }

    public static void DoesNotHideMissingBingClicksBehindOtherSites()
    {
        using var database = CreateDatabase();
        var current = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        database.WriteAsync(c => c.Execute("""
            INSERT INTO bing_rank_traffic(site_url,date,impressions,clicks,raw_json,fetched_at) VALUES
            ('https://missing.example/',@current,50,NULL,'{}','now'),
            ('https://measured.example/',@current,0,0,'{}','now');
            """, new { current })).GetAwaiter().GetResult();
        Require(!InsightEngine.Generate(new InsightMetricsRepository(database).GetInsightMetricsAsync("all", 7).Result)
            .Any(card => card.Priority == 130), "A measured zero on another site must not turn null aggregate clicks into zero");
    }

    private static SqliteDatabase CreateDatabase() => (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
        .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class InsightReviewRegressionTestsInputs
{
    internal static readonly int[] Vector1 = new[] { 5, 10, 20, 25 };
}
