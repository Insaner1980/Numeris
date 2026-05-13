namespace Numeris.Models;

public sealed class ConnectionInfo
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "disconnected";
    public string? LastSync { get; set; }
}
