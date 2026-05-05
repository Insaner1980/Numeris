using System.Collections.Generic;

namespace Numeris.Models;

public sealed class CloudflareConnectionConfig
{
    public string Domain { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string? LastValidatedAt { get; set; }
}

public sealed class CloudflareConnectionInfo
{
    public string Id { get; set; } = "";
    public string Domain { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public bool HasToken { get; set; }
    public string Status { get; set; } = "mock";
    public string? LastSync { get; set; }
    public string? LastValidatedAt { get; set; }
}

public sealed class WebAnalyticsConnectionConfig
{
    public string AccountId { get; set; } = "";
    public string? LastValidatedAt { get; set; }
}

public sealed class WebAnalyticsConnectionInfo
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public bool HasToken { get; set; }
    public string Status { get; set; } = "mock";
    public string? LastSync { get; set; }
}

public sealed class SearchConsoleConnectionConfig
{
    public string ClientId { get; set; } = "";
    public string? LastValidatedAt { get; set; }
    public List<string> Sites { get; set; } = new();
}

public sealed class SearchConsoleConnectionInfo
{
    public string Id { get; set; } = "";
    public string ClientId { get; set; } = "";
    public bool HasClientSecret { get; set; }
    public bool HasRefreshToken { get; set; }
    public string Status { get; set; } = "mock";
    public string? LastSync { get; set; }
}

public sealed class SyncResult
{
    public string Domain { get; set; } = "";
    public long DaysSynced { get; set; }
    public long RecordsUpserted { get; set; }
}
