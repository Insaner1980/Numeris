using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Secrets;

namespace Numeris.Services.Database.Repositories;

public sealed class ConnectionsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SqliteDatabase _db;
    private readonly CredentialVault _vault;

    public ConnectionsRepository(SqliteDatabase db, CredentialVault vault)
    {
        _db = db;
        _vault = vault;
    }

    public Task<List<CloudflareConnectionInfo>> ListCloudflareConnectionsAsync()
    {
        return _db.ReadAsync(connection =>
        {
            var rows = connection.Query<(string Id, string Status, string? Config, string? LastSync)>(
                """
                SELECT id AS Id, status AS Status, config AS Config, last_sync AS LastSync
                FROM connections
                WHERE source = 'cloudflare' AND config IS NOT NULL
                ORDER BY id
                """).AsList();

            var result = new List<CloudflareConnectionInfo>();
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.Config)) continue;
                try
                {
                    var cfg = JsonSerializer.Deserialize<CloudflareConnectionConfig>(row.Config, JsonOptions);
                    if (cfg is null) continue;
                    result.Add(new CloudflareConnectionInfo
                    {
                        Id = row.Id,
                        Domain = cfg.Domain,
                        ZoneId = cfg.ZoneId,
                        HasToken = !string.IsNullOrEmpty(_vault.GetCloudflareToken(cfg.Domain)),
                        Status = row.Status,
                        LastSync = row.LastSync,
                        LastValidatedAt = cfg.LastValidatedAt,
                    });
                }
                catch
                {
                    // skip malformed entries
                }
            }
            return result;
        });
    }

    public Task UpsertCloudflareAsync(CloudflareConnectionConfig config, string status)
    {
        return _db.WriteAsync(connection =>
        {
            var id = $"cloudflare:{config.Domain.Trim().ToLowerInvariant()}";
            var json = JsonSerializer.Serialize(config, JsonOptions);
            connection.Execute(
                """
                INSERT INTO connections (id, source, status, config, last_sync)
                VALUES (@id, 'cloudflare', @status, @json, NULL)
                ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
                """,
                new { id, status, json });
        });
    }

    public Task UpdateCloudflareLastSyncAsync(string domain, string lastSync, string status = "connected")
    {
        return _db.WriteAsync(connection =>
        {
            var id = $"cloudflare:{domain.Trim().ToLowerInvariant()}";
            connection.Execute(
                "UPDATE connections SET status = @status, last_sync = @lastSync WHERE id = @id",
                new { status, lastSync, id });
        });
    }

    public Task<List<ConnectionInfo>> GetAllConnectionStatusesAsync()
    {
        return _db.ReadAsync(connection =>
        {
            return connection.Query<ConnectionInfo>(
                """
                SELECT id AS Id, source AS Source, status AS Status, last_sync AS LastSync
                FROM connections ORDER BY source, id
                """).AsList();
        });
    }

    public Task DeleteCloudflareAsync(string domain)
    {
        return _db.WriteAsync(connection =>
        {
            var id = $"cloudflare:{domain.Trim().ToLowerInvariant()}";
            connection.Execute("DELETE FROM connections WHERE id = @id", new { id });
        });
    }

    public string FormatNow() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    public Task<WebAnalyticsConnectionInfo?> GetWebAnalyticsAsync()
    {
        return _db.ReadAsync(connection =>
        {
            var row = connection.QueryFirstOrDefault<(string Status, string? Config, string? LastSync)>(
                """
                SELECT status AS Status, config AS Config, last_sync AS LastSync
                FROM connections WHERE id = 'wa'
                """);
            WebAnalyticsConnectionConfig? cfg = null;
            if (!string.IsNullOrEmpty(row.Config))
            {
                try { cfg = JsonSerializer.Deserialize<WebAnalyticsConnectionConfig>(row.Config, JsonOptions); }
                catch { }
            }
            return (WebAnalyticsConnectionInfo?)new WebAnalyticsConnectionInfo
            {
                Id = "wa",
                AccountId = cfg?.AccountId ?? "",
                HasToken = !string.IsNullOrEmpty(_vault.GetWebAnalyticsToken(cfg?.AccountId ?? "")),
                Status = row.Status ?? "mock",
                LastSync = row.LastSync,
            };
        });
    }

    public Task UpsertWebAnalyticsAsync(WebAnalyticsConnectionConfig config, string status)
    {
        return _db.WriteAsync(connection =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            connection.Execute(
                """
                INSERT INTO connections (id, source, status, config, last_sync)
                VALUES ('wa', 'web_analytics', @status, @json, NULL)
                ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
                """,
                new { status, json });
        });
    }

    public Task UpdateWebAnalyticsLastSyncAsync(string accountId, string lastSync, string status = "connected")
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                "UPDATE connections SET status = @status, last_sync = @lastSync WHERE id = 'wa'",
                new { status, lastSync });
        });
    }

    public Task DeleteWebAnalyticsAsync()
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute("UPDATE connections SET status = 'mock', config = NULL, last_sync = NULL WHERE id = 'wa'");
        });
    }

    public Task<SearchConsoleConnectionInfo?> GetSearchConsoleAsync()
    {
        return _db.ReadAsync(connection =>
        {
            var row = connection.QueryFirstOrDefault<(string Status, string? Config, string? LastSync)>(
                "SELECT status AS Status, config AS Config, last_sync AS LastSync FROM connections WHERE id = 'sc'");
            SearchConsoleConnectionConfig? cfg = null;
            if (!string.IsNullOrEmpty(row.Config))
            {
                try { cfg = JsonSerializer.Deserialize<SearchConsoleConnectionConfig>(row.Config, JsonOptions); }
                catch { }
            }
            var clientId = cfg?.ClientId ?? "";
            return (SearchConsoleConnectionInfo?)new SearchConsoleConnectionInfo
            {
                Id = "sc",
                ClientId = clientId,
                HasClientSecret = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(_vault.GetSearchConsoleClientSecret(clientId)),
                HasRefreshToken = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(_vault.GetSearchConsoleRefreshToken(clientId)),
                Status = row.Status ?? "mock",
                LastSync = row.LastSync,
            };
        });
    }

    public Task UpsertSearchConsoleAsync(SearchConsoleConnectionConfig config, string status)
    {
        return _db.WriteAsync(connection =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            connection.Execute(
                """
                INSERT INTO connections (id, source, status, config, last_sync)
                VALUES ('sc', 'search_console', @status, @json, NULL)
                ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
                """,
                new { status, json });
        });
    }

    public Task UpdateSearchConsoleLastSyncAsync(string clientId, string lastSync, string status = "connected")
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                "UPDATE connections SET status = @status, last_sync = @lastSync WHERE id = 'sc'",
                new { status, lastSync });
        });
    }

    public Task DeleteSearchConsoleAsync()
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute("UPDATE connections SET status = 'mock', config = NULL, last_sync = NULL WHERE id = 'sc'");
        });
    }

    public Task<PerformanceConnectionInfo?> GetPerformanceAsync()
    {
        return _db.ReadAsync(connection =>
        {
            var row = connection.QueryFirstOrDefault<(string Status, string? Config, string? LastSync)>(
                "SELECT status AS Status, config AS Config, last_sync AS LastSync FROM connections WHERE id = 'perf'");
            return (PerformanceConnectionInfo?)new PerformanceConnectionInfo
            {
                Id = "perf",
                HasCruxApiKey = !string.IsNullOrEmpty(_vault.GetCruxApiKey()),
                HasPageSpeedApiKey = !string.IsNullOrEmpty(_vault.GetPageSpeedApiKey()),
                Status = row.Status ?? "mock",
                LastSync = row.LastSync,
            };
        });
    }

    public Task UpsertPerformanceAsync(PerformanceConnectionConfig config, string status)
    {
        return _db.WriteAsync(connection =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            connection.Execute(
                """
                INSERT INTO connections (id, source, status, config, last_sync)
                VALUES ('perf', 'performance', @status, @json, NULL)
                ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
                """,
                new { status, json });
        });
    }

    public Task UpdatePerformanceLastSyncAsync(string lastSync, string status = "connected")
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                "UPDATE connections SET status = @status, last_sync = @lastSync WHERE id = 'perf'",
                new { status, lastSync });
        });
    }

    public Task DeletePerformanceAsync()
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute("UPDATE connections SET status = 'mock', config = NULL, last_sync = NULL WHERE id = 'perf'");
        });
    }

    public Task<BingConnectionInfo?> GetBingAsync()
    {
        return _db.ReadAsync(connection =>
        {
            var row = connection.QueryFirstOrDefault<(string Status, string? Config, string? LastSync)>(
                "SELECT status AS Status, config AS Config, last_sync AS LastSync FROM connections WHERE id = 'bing'");
            BingConnectionConfig? cfg = null;
            if (!string.IsNullOrEmpty(row.Config))
            {
                try { cfg = JsonSerializer.Deserialize<BingConnectionConfig>(row.Config, JsonOptions); }
                catch { }
            }
            return (BingConnectionInfo?)new BingConnectionInfo
            {
                Id = "bing",
                HasApiKey = !string.IsNullOrEmpty(_vault.GetBingApiKey()),
                Sites = cfg?.Sites ?? new List<string>(),
                Status = row.Status ?? "mock",
                LastSync = row.LastSync,
            };
        });
    }

    public Task UpsertBingAsync(BingConnectionConfig config, string status)
    {
        return _db.WriteAsync(connection =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            connection.Execute(
                """
                INSERT INTO connections (id, source, status, config, last_sync)
                VALUES ('bing', 'bing_webmaster', @status, @json, NULL)
                ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
                """,
                new { status, json });
        });
    }

    public Task UpdateBingLastSyncAsync(string lastSync, string status = "connected")
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                "UPDATE connections SET status = @status, last_sync = @lastSync WHERE id = 'bing'",
                new { status, lastSync });
        });
    }

    public Task DeleteBingAsync()
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute("UPDATE connections SET status = 'mock', config = NULL, last_sync = NULL WHERE id = 'bing'");
        });
    }
}
