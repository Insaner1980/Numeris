using System.Collections.Generic;

namespace Numeris.Models;

public sealed class CloudflareConnectionConfig
{
    public string Domain { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string? LastValidatedAt { get; set; }
    public string? ImportSource { get; set; }
}

public sealed class CloudflareConnectionInfo
{
    public string Id { get; set; } = "";
    public string Domain { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public bool HasToken { get; set; }
    public string Status { get; set; } = "disconnected";
    public string? LastSync { get; set; }
    public string? LastValidatedAt { get; set; }
}

public sealed class WebAnalyticsConnectionConfig
{
    public string AccountId { get; set; } = "";
    public string? LastValidatedAt { get; set; }
    public string? ImportSource { get; set; }
}

public sealed class WebAnalyticsConnectionInfo
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public bool HasToken { get; set; }
    public string Status { get; set; } = "disconnected";
    public string? LastSync { get; set; }
}

public sealed class SearchConsoleConnectionConfig
{
    public string ClientId { get; set; } = "";
    public string? LastValidatedAt { get; set; }
    public List<string> Sites { get; set; } = new();
    public string? ImportSource { get; set; }
}

public sealed class SearchConsoleConnectionInfo
{
    public string Id { get; set; } = "";
    public string ClientId { get; set; } = "";
    public bool HasClientSecret { get; set; }
    public bool HasRefreshToken { get; set; }
    public string Status { get; set; } = "disconnected";
    public string? LastSync { get; set; }
}

public sealed class PerformanceConnectionConfig
{
    public string? LastValidatedAt { get; set; }
    public string? ImportSource { get; set; }
}

public sealed class PerformanceConnectionInfo
{
    public string Id { get; set; } = "perf";
    public bool HasCruxApiKey { get; set; }
    public bool HasPageSpeedApiKey { get; set; }
    public string Status { get; set; } = "disconnected";
    public string? LastSync { get; set; }
}

public sealed class PerformanceUrlInfo
{
    public long Id { get; set; }
    public string Url { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Source { get; set; } = "manual";
    public bool Enabled { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class BingConnectionConfig
{
    public List<string> Sites { get; set; } = new();
    public string? LastValidatedAt { get; set; }
    public string? ImportSource { get; set; }
}

public sealed class BingConnectionInfo
{
    public string Id { get; set; } = "bing";
    public bool HasApiKey { get; set; }
    public List<string> Sites { get; set; } = new();
    public string Status { get; set; } = "disconnected";
    public string? LastSync { get; set; }
}

public sealed class SyncResult
{
    public string Domain { get; set; } = "";
    public long DaysSynced { get; set; }
    public long RecordsUpserted { get; set; }
}

public sealed class IndexingInspectionResult
{
    public string Domain { get; set; } = "";
    public long TotalUrls { get; set; }
    public long UrlsChecked { get; set; }
    public long Indexed { get; set; }
    public long NotIndexed { get; set; }
    public long Errors { get; set; }
    public string? FirstError { get; set; }
}

public sealed class ConnectionTestResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}
