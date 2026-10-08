namespace Numeris.Models;

public sealed class UptimeDomainStatus
{
    public string Domain { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? LastCheckedAt { get; set; }
    public int? StatusCode { get; set; }
    public long? ResponseMs { get; set; }
    public double UptimePct { get; set; }
    public long Incidents { get; set; }
}

public sealed class UptimeCheckDay
{
    public string Domain { get; set; } = "";
    public string Date { get; set; } = "";
    public double UptimePct { get; set; }
    public double? AvgResponseMs { get; set; }
    public long Incidents { get; set; }
}

public sealed class SitemapRefreshResult
{
    public string Domain { get; set; } = "";
    public long TotalUrls { get; set; }
    public long NewUrls { get; set; }
    public long RemovedUrls { get; set; }
}
