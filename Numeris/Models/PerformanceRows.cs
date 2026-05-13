namespace Numeris.Models;

public sealed class CruxMetricPoint
{
    public string TargetType { get; set; } = "";
    public string Target { get; set; } = "";
    public string FormFactor { get; set; } = "";
    public string CollectionStart { get; set; } = "";
    public string CollectionEnd { get; set; } = "";
    public string Metric { get; set; } = "";
    public double? P75 { get; set; }
    public double? GoodDensity { get; set; }
    public double? NeedsImprovementDensity { get; set; }
    public double? PoorDensity { get; set; }
    public string RawJson { get; set; } = "";
    public string FetchedAt { get; set; } = "";
}

public sealed class CruxMetricSummary
{
    public string TargetType { get; set; } = "";
    public string Target { get; set; } = "";
    public string FormFactor { get; set; } = "";
    public string Metric { get; set; } = "";
    public string LatestCollectionEnd { get; set; } = "";
    public double? P75 { get; set; }
    public double? GoodDensity { get; set; }
    public double? NeedsImprovementDensity { get; set; }
    public double? PoorDensity { get; set; }
    public string Status { get; set; } = "No data";
}

public sealed class CruxTrendPoint
{
    public string CollectionEnd { get; set; } = "";
    public string Metric { get; set; } = "";
    public string FormFactor { get; set; } = "";
    public double? P75 { get; set; }
}

public sealed class PageSpeedRun
{
    public string Url { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string AnalysisUtc { get; set; } = "";
    public string? FinalUrl { get; set; }
    public double? PerformanceScore { get; set; }
    public double? AccessibilityScore { get; set; }
    public double? BestPracticesScore { get; set; }
    public double? SeoScore { get; set; }
    public string? LighthouseVersion { get; set; }
    public string? RuntimeError { get; set; }
    public string? WarningsJson { get; set; }
    public string RawJson { get; set; } = "";
    public string FetchedAt { get; set; } = "";
}

public sealed class PageSpeedLatestRun
{
    public string Url { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string AnalysisUtc { get; set; } = "";
    public string? FinalUrl { get; set; }
    public double? PerformanceScore { get; set; }
    public double? AccessibilityScore { get; set; }
    public double? BestPracticesScore { get; set; }
    public double? SeoScore { get; set; }
    public string? RuntimeError { get; set; }
}

public sealed class PageSpeedScorePoint
{
    public string AnalysisUtc { get; set; } = "";
    public string Strategy { get; set; } = "";
    public double? PerformanceScore { get; set; }
}

public sealed class PageSpeedAudit
{
    public string Url { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string AnalysisUtc { get; set; } = "";
    public string AuditId { get; set; } = "";
    public string? Title { get; set; }
    public double? Score { get; set; }
    public double? NumericValue { get; set; }
    public string? NumericUnit { get; set; }
    public string? DisplayValue { get; set; }
    public string? ScoreDisplayMode { get; set; }
    public string? DetailsJson { get; set; }
}

public sealed class PageSpeedAuditIssue
{
    public string Url { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string AuditId { get; set; } = "";
    public string Title { get; set; } = "";
    public double? Score { get; set; }
    public string DisplayValue { get; set; } = "";
    public double? NumericValue { get; set; }
    public string NumericUnit { get; set; } = "";
}

public sealed class PerformanceSyncResult
{
    public long UrlsSynced { get; set; }
    public long CruxMetricPoints { get; set; }
    public long CruxSkipped { get; set; }
    public long PageSpeedRuns { get; set; }
    public long PageSpeedAudits { get; set; }
    public long PageSpeedErrors { get; set; }
}
