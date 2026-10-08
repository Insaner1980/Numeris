namespace Numeris.Models;

public sealed class TrafficDay
{
    public string Date { get; set; } = "";
    public long Pageviews { get; set; }
    public long UniqueVisitors { get; set; }
}

public sealed class SearchDay
{
    public string Date { get; set; } = "";
    public long TotalClicks { get; set; }
    public long TotalImpressions { get; set; }
    public double AvgPosition { get; set; }
}

public sealed class CacheDay
{
    public string Date { get; set; } = "";
    public long CachedRequests { get; set; }
    public long TotalRequests { get; set; }
    public long CachedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double HitRatio { get; set; }
}

public sealed class SecurityDay
{
    public string Date { get; set; } = "";
    public long Threats { get; set; }
    public long TotalRequests { get; set; }
}

public sealed class SitemapUrl
{
    public string Domain { get; set; } = "";
    public string Url { get; set; } = "";
    public string DiscoveredAt { get; set; } = "";
    public string LastSeenAt { get; set; } = "";
    public string? RemovedAt { get; set; }
    public string? Verdict { get; set; }
    public string? CoverageState { get; set; }
    public string? IndexingState { get; set; }
    public string? LastInspectedAt { get; set; }
    public string? LastCrawlTime { get; set; }
    public string? PageFetchState { get; set; }
    public string? CrawledAs { get; set; }

    public bool HasInspectionData =>
        !string.IsNullOrWhiteSpace(Verdict)
        || !string.IsNullOrWhiteSpace(CoverageState)
        || !string.IsNullOrWhiteSpace(IndexingState);

    public bool IsIndexed =>
        string.Equals(Verdict, "PASS", System.StringComparison.OrdinalIgnoreCase)
        || (CoverageState?.StartsWith("Indexed", System.StringComparison.OrdinalIgnoreCase) ?? false);

    public string IndexingStatusText => (HasInspectionData, IsIndexed) switch
    {
        (false, _) => "",
        (_, true) => "Indexed",
        _ => "Not indexed",
    };

    public string CoverageStatusText => string.IsNullOrWhiteSpace(CoverageState)
        ? "—"
        : CoverageState;

    public string LastCrawlText => string.IsNullOrWhiteSpace(LastCrawlTime)
        ? "—"
        : LastCrawlTime;
}
