using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
                Status = row.Status ?? "disconnected",
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
            connection.Execute("UPDATE connections SET status = 'disconnected', config = NULL, last_sync = NULL WHERE id = 'wa'");
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
                Status = row.Status ?? "disconnected",
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
            connection.Execute("UPDATE connections SET status = 'disconnected', config = NULL, last_sync = NULL WHERE id = 'sc'");
        });
    }

    public Task<GoogleAnalyticsConnectionInfo?> GetGoogleAnalyticsAsync()
    {
        return _db.ReadAsync(connection =>
        {
            var row = connection.QueryFirstOrDefault<(string Status, string? Config, string? LastSync)>(
                "SELECT status AS Status, config AS Config, last_sync AS LastSync FROM connections WHERE id = 'ga4'");
            GoogleAnalyticsConnectionConfig? cfg = null;
            if (!string.IsNullOrEmpty(row.Config))
            {
                try { cfg = JsonSerializer.Deserialize<GoogleAnalyticsConnectionConfig>(row.Config, JsonOptions); }
                catch { }
            }

            var clientId = cfg?.ClientId ?? "";
            return (GoogleAnalyticsConnectionInfo?)new GoogleAnalyticsConnectionInfo
            {
                Id = "ga4",
                Domain = cfg?.Domain ?? "",
                PropertyId = cfg?.PropertyId ?? "",
                ClientId = clientId,
                HasClientSecret = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(_vault.GetGoogleAnalyticsClientSecret(clientId)),
                HasRefreshToken = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(_vault.GetGoogleAnalyticsRefreshToken(clientId)),
                Status = row.Status ?? "disconnected",
                LastSync = row.LastSync,
            };
        });
    }

    public Task UpsertGoogleAnalyticsAsync(GoogleAnalyticsConnectionConfig config, string status)
    {
        return _db.WriteAsync(connection =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            connection.Execute(
                """
                INSERT INTO connections (id, source, status, config, last_sync)
                VALUES ('ga4', 'google_analytics', @status, @json, NULL)
                ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
                """,
                new { status, json });
        });
    }

    public Task UpdateGoogleAnalyticsLastSyncAsync(GoogleAnalyticsConnectionConfig config, string lastSync, string status = "connected")
    {
        return _db.WriteAsync(connection =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            connection.Execute(
                """
                UPDATE connections
                SET status = @status, config = @json, last_sync = @lastSync
                WHERE id = 'ga4'
                """,
                new { status, json, lastSync });
        });
    }

    public Task DeleteGoogleAnalyticsAsync()
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute("UPDATE connections SET status = 'disconnected', config = NULL, last_sync = NULL WHERE id = 'ga4'");
        });
    }

    public Task<PerformanceConnectionInfo?> GetPerformanceAsync()
    {
        return _db.ReadAsync(connection =>
        {
            var rows = connection.Query<(string Id, string Status, string? LastSync)>(
                """
                SELECT id AS Id, status AS Status, last_sync AS LastSync
                FROM connections
                WHERE id IN ('perf', 'crux', 'pagespeed')
                """).AsList();
            var connected = rows.Exists(r => r.Status == "connected");
            var configured = rows.Exists(r => r.Status == "configured");
            return (PerformanceConnectionInfo?)new PerformanceConnectionInfo
            {
                Id = "perf",
                HasCruxApiKey = !string.IsNullOrEmpty(_vault.GetCruxApiKey()),
                HasPageSpeedApiKey = !string.IsNullOrEmpty(_vault.GetPageSpeedApiKey()),
                Status = connected ? "connected" : configured ? "configured" : "disconnected",
                LastSync = rows.Select(r => r.LastSync).Where(v => !string.IsNullOrWhiteSpace(v)).OrderByDescending(v => v).FirstOrDefault(),
            };
        });
    }

    public Task UpsertPerformanceAsync(PerformanceConnectionConfig config, string status)
    {
        return _db.WriteAsync(connection =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            UpsertSingletonConnection(connection, "perf", "performance", status, json);
            if (!string.IsNullOrWhiteSpace(_vault.GetCruxApiKey()))
            {
                UpsertSingletonConnection(connection, "crux", "crux", status, json);
            }
            if (!string.IsNullOrWhiteSpace(_vault.GetPageSpeedApiKey()))
            {
                UpsertSingletonConnection(connection, "pagespeed", "pagespeed", status, json);
            }
        });
    }

    public Task UpdatePerformanceLastSyncAsync(string lastSync, string status = "connected")
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                """
                UPDATE connections
                SET status = @status, last_sync = @lastSync
                WHERE id = 'perf'
                   OR (id = 'crux' AND @hasCrux = 1)
                   OR (id = 'pagespeed' AND @hasPageSpeed = 1)
                """,
                new
                {
                    status,
                    lastSync,
                    hasCrux = string.IsNullOrWhiteSpace(_vault.GetCruxApiKey()) ? 0 : 1,
                    hasPageSpeed = string.IsNullOrWhiteSpace(_vault.GetPageSpeedApiKey()) ? 0 : 1,
                });
        });
    }

    private static void UpsertSingletonConnection(Microsoft.Data.Sqlite.SqliteConnection connection, string id, string source, string status, string json)
    {
        connection.Execute(
            """
            INSERT INTO connections (id, source, status, config, last_sync)
            VALUES (@id, @source, @status, @json, NULL)
            ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
            """,
            new { id, source, status, json });
    }

    public Task DeletePerformanceAsync()
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                "UPDATE connections SET status = 'disconnected', config = NULL, last_sync = NULL WHERE id IN ('perf', 'crux', 'pagespeed')");
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
                Status = row.Status ?? "disconnected",
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
            connection.Execute("UPDATE connections SET status = 'disconnected', config = NULL, last_sync = NULL WHERE id = 'bing'");
        });
    }
}
