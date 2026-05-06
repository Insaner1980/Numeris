namespace Numeris.Models;

public sealed class BingRawItem
{
    public string Method { get; set; } = "";
    public string SiteUrl { get; set; } = "";
    public string ItemKey { get; set; } = "";
    public string RawJson { get; set; } = "";
    public string FetchedAt { get; set; } = "";
}

public sealed class BingSyncResult
{
    public long SitesSynced { get; set; }
    public long RawItems { get; set; }
    public long RankRows { get; set; }
    public long QueryRows { get; set; }
    public long PageRows { get; set; }
}
