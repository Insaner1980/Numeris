using System.Globalization;

namespace Numeris.Models;

public sealed class GoogleAnalyticsDailyRow
{
    public string Date { get; set; } = "";
    public long ActiveUsers { get; set; }
    public long Sessions { get; set; }
    public long PageViews { get; set; }
    public long EngagedSessions { get; set; }
    public long EventCount { get; set; }
    public double EngagementRate { get; set; }
}

public sealed class GoogleAnalyticsPageRow
{
    public string PagePath { get; set; } = "";
    public long ActiveUsers { get; set; }
    public long Sessions { get; set; }
    public long PageViews { get; set; }
    public long EngagedSessions { get; set; }
    public double EngagementRate { get; set; }
    public string EngagedSessionsText => EngagedSessions.ToString("N0", CultureInfo.CurrentCulture);
    public string EngagementRateText => EngagementRate.ToString("P1", CultureInfo.CurrentCulture);
}

public sealed class GoogleAnalyticsSourceRow
{
    public string SourceMedium { get; set; } = "";
    public long Sessions { get; set; }
    public long ActiveUsers { get; set; }
    public long KeyEvents { get; set; }
}

public sealed class GoogleAnalyticsEventRow
{
    public string EventName { get; set; } = "";
    public long EventCount { get; set; }
    public long KeyEvents { get; set; }
}

public sealed class GoogleAnalyticsDeviceRow
{
    public string Date { get; set; } = "";
    public string DeviceCategory { get; set; } = "";
    public long Sessions { get; set; }
    public long ActiveUsers { get; set; }
}
