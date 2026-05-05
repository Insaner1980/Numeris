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
