namespace Numeris.Models;

public sealed class CountryData
{
    public string Country { get; set; } = "";
    public long Value { get; set; }
}

public sealed class PageData
{
    public string Path { get; set; } = "";
    public long Pageviews { get; set; }
    public double Percentage { get; set; }
}

public sealed class StatusCodeDay
{
    public string Date { get; set; } = "";
    public int StatusCode { get; set; }
    public long Requests { get; set; }
}

public sealed class WebAnalyticsDay
{
    public string Date { get; set; } = "";
    public long Visits { get; set; }
    public long PageViews { get; set; }
}

public sealed class WebAnalyticsReferrer
{
    public string Referrer { get; set; } = "";
    public long Visits { get; set; }
}

public sealed class WebAnalyticsPage
{
    public string Path { get; set; } = "";
    public long PageViews { get; set; }
}
