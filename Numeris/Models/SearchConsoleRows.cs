namespace Numeris.Models;

public sealed class SearchQuery
{
    public string Query { get; set; } = "";
    public long Clicks { get; set; }
    public long Impressions { get; set; }
    public double Ctr { get; set; }
    public double Position { get; set; }
}

public sealed class SearchPageRow
{
    public string Page { get; set; } = "";
    public long Clicks { get; set; }
    public long Impressions { get; set; }
}

public sealed class SearchDeviceDay
{
    public string Date { get; set; } = "";
    public string Device { get; set; } = "";
    public long Clicks { get; set; }
    public long Impressions { get; set; }
    public double Ctr { get; set; }
    public double Position { get; set; }
}

public sealed class PageQueryRow
{
    public string Page { get; set; } = "";
    public string Query { get; set; } = "";
    public long Clicks { get; set; }
    public long Impressions { get; set; }
    public double Ctr { get; set; }
    public double Position { get; set; }
}

public sealed class DecliningPage
{
    public string Page { get; set; } = "";
    public long ClicksCurrent { get; set; }
    public long ClicksPrevious { get; set; }
    public long Delta { get; set; }
}
