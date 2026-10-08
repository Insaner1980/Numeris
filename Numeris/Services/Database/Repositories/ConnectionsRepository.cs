using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Secrets;

namespace Numeris.Services.Database.Repositories;

public sealed class ConnectionsRepository
{
    private const string ConnectedStatus = "connected";
    private const string DisconnectedStatus = "disconnected";

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

    public Task<List<string>> ListConfiguredDomainsAsync()
        => _db.ReadAsync(connection =>
        {
            var values = connection.Query<string>(
                """
                SELECT json_extract(config, '$.domain') AS domain
                FROM connections WHERE json_valid(config)
                UNION
                SELECT value FROM connections,
                    json_each(CASE WHEN json_valid(config) THEN config ELSE '{}' END, '$.sites')
                WHERE connections.source = 'search_console' AND json_each.type = 'text'
                UNION SELECT origin FROM performance_urls WHERE enabled = 1
                UNION SELECT site_url FROM bing_sites WHERE enabled = 1
                UNION SELECT domain FROM web_analytics_sites
                ORDER BY domain
                """);
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                var site = value.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase)
                    ? value["sc-domain:".Length..]
                    : value;
                try { domains.Add(SiteIdentity.NormalizeDomain(site)); }
                catch (ArgumentException) { /* Ignore malformed legacy site metadata when building selectable domains. */ }
            }
            return domains.OrderBy(domain => domain, StringComparer.Ordinal).ToList();
        });

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
                catch (Exception ex) when (ex is JsonException or ArgumentException)
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

    public Task UpdateCloudflareLastSyncAsync(string domain, string lastSync, string status = ConnectedStatus)
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

    public static string FormatNow() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

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
                catch (JsonException) { /* Corrupt legacy metadata is shown as an unconfigured connection. */ }
            }
            return (WebAnalyticsConnectionInfo?)new WebAnalyticsConnectionInfo
            {
                Id = "wa",
                AccountId = cfg?.AccountId ?? "",
                HasToken = !string.IsNullOrEmpty(_vault.GetWebAnalyticsToken(cfg?.AccountId ?? "")),
                Status = row.Status ?? DisconnectedStatus,
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

    public Task UpdateWebAnalyticsLastSyncAsync(string accountId, string lastSync, string status = ConnectedStatus)
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
                catch (JsonException) { /* Corrupt legacy metadata is shown as an unconfigured connection. */ }
            }
            var clientId = cfg?.ClientId ?? "";
            return (SearchConsoleConnectionInfo?)new SearchConsoleConnectionInfo
            {
                Id = "sc",
                ClientId = clientId,
                HasClientSecret = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(_vault.GetSearchConsoleClientSecret(clientId)),
                HasRefreshToken = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(_vault.GetSearchConsoleRefreshToken(clientId)),
                Status = row.Status ?? DisconnectedStatus,
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
                ON CONFLICT(id) DO UPDATE SET status = excluded.status,
                    config = CASE WHEN json_valid(connections.config) THEN
                        CASE WHEN json_extract(connections.config, '$.clientId') = @clientId
                            AND json_type(connections.config, '$.sites') = 'array'
                            AND json_array_length(@json, '$.sites') = 0
                        THEN json_set(excluded.config, '$.sites', json(json_extract(connections.config, '$.sites')))
                        ELSE excluded.config END
                    ELSE excluded.config END
                """,
                new { status, json, clientId = config.ClientId });
        });
    }

    public Task UpdateSearchConsoleLastSyncAsync(string clientId, string lastSync, string status = ConnectedStatus)
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
            var connected = rows.Exists(r => r.Status == ConnectedStatus);
            var configured = rows.Exists(r => r.Status == "configured");
            return (PerformanceConnectionInfo?)new PerformanceConnectionInfo
            {
                Id = "perf",
                HasCruxApiKey = !string.IsNullOrEmpty(_vault.GetCruxApiKey()),
                HasPageSpeedApiKey = !string.IsNullOrEmpty(_vault.GetPageSpeedApiKey()),
                Status = (connected, configured) switch
                {
                    (true, _) => ConnectedStatus,
                    (_, true) => "configured",
                    _ => DisconnectedStatus,
                },
                LastSync = rows.Select(r => r.LastSync).Where(v => !string.IsNullOrWhiteSpace(v)).OrderByDescending(v => v).FirstOrDefault(),
            };
        });
    }

    public Task UpsertPerformanceAsync(PerformanceConnectionConfig config, string status)
    {
        return _db.WriteTransactionAsync((connection, transaction) =>
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            UpsertSingletonConnection(connection, "perf", "performance", status, json, transaction);
            if (!string.IsNullOrWhiteSpace(_vault.GetCruxApiKey()))
            {
                UpsertSingletonConnection(connection, "crux", "crux", status, json, transaction);
            }
            if (!string.IsNullOrWhiteSpace(_vault.GetPageSpeedApiKey()))
            {
                UpsertSingletonConnection(connection, "pagespeed", "pagespeed", status, json, transaction);
            }
        });
    }

    public Task UpdatePerformanceLastSyncAsync(string lastSync, string status = ConnectedStatus)
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

    private static void UpsertSingletonConnection(Microsoft.Data.Sqlite.SqliteConnection connection, string id, string source, string status, string json, Microsoft.Data.Sqlite.SqliteTransaction? transaction = null)
    {
        connection.Execute(
            """
            INSERT INTO connections (id, source, status, config, last_sync)
            VALUES (@id, @source, @status, @json, NULL)
            ON CONFLICT(id) DO UPDATE SET status = excluded.status, config = excluded.config
            """,
            new { id, source, status, json }, transaction);
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
                catch (JsonException) { /* Corrupt legacy metadata is shown as an unconfigured connection. */ }
            }
            return (BingConnectionInfo?)new BingConnectionInfo
            {
                Id = "bing",
                HasApiKey = !string.IsNullOrEmpty(_vault.GetBingApiKey()),
                Sites = cfg?.Sites ?? new List<string>(),
                Status = row.Status ?? DisconnectedStatus,
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

    public Task UpdateBingLastSyncAsync(string lastSync, string status = ConnectedStatus)
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
