using System;
using System.Collections.Generic;

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

public enum StatusCodeGroup
{
    Success,
    Redirect,
    ClientError,
    ServerError,
    Other,
}

public sealed class StatusCodeGroupTrend
{
    public StatusCodeGroup Group { get; set; }
    public string Label { get; set; } = "";
    public long Total { get; set; }
    public long[] Values { get; set; } = Array.Empty<long>();
}

public sealed class StatusCodeIssueRow
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string RequestsText { get; set; } = "";
    public string ShareText { get; set; } = "";
}

public sealed class StatusCodeInsight
{
    public string[] Labels { get; set; } = Array.Empty<string>();
    public List<StatusCodeGroupTrend> Groups { get; set; } = new();
    public List<StatusCodeIssueRow> TopCodes { get; set; } = new();
    public string SuccessRateText { get; set; } = "—";
    public string ClientErrorsText { get; set; } = "—";
    public string ServerErrorsText { get; set; } = "—";
    public string TopIssueText { get; set; } = "None";
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
