namespace Numeris.Models;

public sealed class ConnectionInfo
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "mock";
    public string? LastSync { get; set; }
}

public sealed class SeedResult
{
    public int DaysGenerated { get; set; }
    public long RecordsInserted { get; set; }
}
