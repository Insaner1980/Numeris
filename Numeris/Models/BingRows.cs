namespace Numeris.Models;

public sealed class BingRawItem
{
    public string Method { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string ItemKey { get; set; } = "";
    public string RawJson { get; set; } = "";
    public string FetchedAt { get; set; } = "";
}

public sealed class BingTrafficDay
{
    public string Date { get; set; } = "";
    public long Clicks { get; set; }
    public long Impressions { get; set; }
    public double Ctr { get; set; }
}

public sealed class BingQueryRow
{
    public string Query { get; set; } = "";
    public long Clicks { get; set; }
    public long Impressions { get; set; }
    public double Ctr { get; set; }
    public double AvgClickPosition { get; set; }
    public double AvgImpressionPosition { get; set; }
}

public sealed class BingPageRow
{
    public string PageUrl { get; set; } = "";
    public long Clicks { get; set; }
    public long Impressions { get; set; }
    public double Ctr { get; set; }
}

public sealed class BingRawMethodSummary
{
    public string Method { get; set; } = "";
    public long ItemCount { get; set; }
    public string LastFetchedAt { get; set; } = "";
}

public sealed class BingSyncResult
{
    public long SitesSynced { get; set; }
    public long RawItems { get; set; }
    public long RankRows { get; set; }
    public long QueryRows { get; set; }
    public long PageRows { get; set; }
}
