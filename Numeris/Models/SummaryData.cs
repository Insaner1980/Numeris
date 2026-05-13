namespace Numeris.Models;

public sealed class SummaryData
{
    public long VisitorsTotal { get; set; }
    public double VisitorsChangePct { get; set; }
    public long ClicksTotal { get; set; }
    public double ClicksChangePct { get; set; }
    public long InstallsTotal { get; set; }
    public double InstallsChangePct { get; set; }
    public double RevenueTotal { get; set; }
    public double RevenueChangePct { get; set; }
    public double AverageRating { get; set; }
    public double RatingChange { get; set; }
    public double CrashRate { get; set; }
    public double CrashRateChange { get; set; }
}

public sealed class BingOverviewSummary
{
    public long Clicks { get; set; }
    public long PreviousClicks { get; set; }
    public long Impressions { get; set; }
    public double ClicksChangePct { get; set; }
}

public sealed class WebVitalsOverviewSummary
{
    public string Status { get; set; } = "No data";
    public string Detail { get; set; } = "No CrUX data";
}

public sealed class PageSpeedOverviewSummary
{
    public double? MobilePerformanceScore { get; set; }
    public string AnalysisUtc { get; set; } = "";
}
