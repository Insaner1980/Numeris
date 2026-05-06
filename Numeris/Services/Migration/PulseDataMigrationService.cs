using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Models;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Migration;

public sealed class PulseDataMigrationService
{
    private const string ImportSource = "pulse-tauri";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SqliteDatabase _db;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public PulseDataMigrationService(SqliteDatabase db, CredentialVault vault, ConnectionsRepository connectionsRepo)
    {
        _db = db;
        _vault = vault;
        _connectionsRepo = connectionsRepo;
    }

    public async Task ImportAllAsync()
    {
        var pulseDbPath = GetPulseDatabasePath();
        if (!File.Exists(pulseDbPath))
        {
            return;
        }

        await ImportWebAnalyticsAsync(pulseDbPath).ConfigureAwait(false);
        await ImportCloudflareAsync(pulseDbPath).ConfigureAwait(false);
        await ImportSearchConsoleAsync(pulseDbPath).ConfigureAwait(false);
    }

    public async Task ImportWebAnalyticsAsync()
    {
        var pulseDbPath = GetPulseDatabasePath();
        if (!File.Exists(pulseDbPath))
        {
            return;
        }

        await ImportWebAnalyticsAsync(pulseDbPath).ConfigureAwait(false);
    }

    private async Task ImportWebAnalyticsAsync(string pulseDbPath)
    {
        var pulse = ReadPulseWebAnalytics(pulseDbPath);
        if (pulse is null || string.IsNullOrWhiteSpace(pulse.AccountId))
        {
            return;
        }

        var apiToken = FirstNonBlank(pulse.ApiToken, PulseCredentialReader.ReadWebAnalyticsToken(pulse.AccountId));
        if (!string.IsNullOrWhiteSpace(apiToken))
        {
            _vault.SetWebAnalyticsToken(pulse.AccountId, NormalizeCloudflareToken(apiToken));
        }

        await UpsertImportedConnectionAsync(
            "wa",
            "web_analytics",
            NormalizeStatus(pulse.Status),
            new WebAnalyticsConnectionConfig
            {
                AccountId = pulse.AccountId,
                LastValidatedAt = pulse.LastValidatedAt ?? _connectionsRepo.FormatNow(),
                ImportSource = ImportSource,
            }).ConfigureAwait(false);

        if (pulse.Sites.Count == 0)
        {
            return;
        }

        var nowStr = _connectionsRepo.FormatNow();
        await _db.WriteAsync(connection =>
        {
            foreach (var site in pulse.Sites)
            {
                connection.Execute(
                    """
                    INSERT INTO web_analytics_sites (domain, site_tag, discovered_at)
                    VALUES (@domain, @siteTag, @discoveredAt)
                    ON CONFLICT(domain) DO UPDATE SET
                        site_tag = excluded.site_tag,
                        discovered_at = excluded.discovered_at
                    """,
                    new
                    {
                        domain = site.Domain,
                        siteTag = site.SiteTag,
                        discoveredAt = string.IsNullOrWhiteSpace(site.DiscoveredAt) ? nowStr : site.DiscoveredAt,
                    });
            }
        }).ConfigureAwait(false);
    }

    private async Task ImportCloudflareAsync(string pulseDbPath)
    {
        foreach (var pulse in ReadPulseCloudflareConnections(pulseDbPath))
        {
            if (string.IsNullOrWhiteSpace(pulse.Domain) || string.IsNullOrWhiteSpace(pulse.ZoneId))
            {
                continue;
            }

            var domain = pulse.Domain.Trim().ToLowerInvariant();
            var apiToken = FirstNonBlank(pulse.ApiToken, PulseCredentialReader.ReadCloudflareToken(domain));
            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                _vault.SetCloudflareToken(domain, NormalizeCloudflareToken(apiToken));
            }

            await UpsertImportedConnectionAsync(
                $"cloudflare:{domain}",
                "cloudflare",
                NormalizeStatus(pulse.Status),
                new CloudflareConnectionConfig
                {
                    Domain = domain,
                    ZoneId = pulse.ZoneId.Trim(),
                    LastValidatedAt = pulse.LastValidatedAt,
                    ImportSource = ImportSource,
                }).ConfigureAwait(false);
        }
    }

    private async Task ImportSearchConsoleAsync(string pulseDbPath)
    {
        var pulse = ReadPulseSearchConsole(pulseDbPath);
        if (pulse is null || string.IsNullOrWhiteSpace(pulse.ClientId))
        {
            return;
        }

        var clientId = pulse.ClientId.Trim();
        var clientSecret = FirstNonBlank(pulse.ClientSecret, PulseCredentialReader.ReadSearchConsoleClientSecret(clientId));
        var refreshToken = FirstNonBlank(pulse.RefreshToken, PulseCredentialReader.ReadSearchConsoleRefreshToken(clientId));

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            _vault.SetSearchConsoleClientSecret(clientId, clientSecret);
        }
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            _vault.SetSearchConsoleRefreshToken(clientId, refreshToken);
        }

        await UpsertImportedConnectionAsync(
            "sc",
            "search_console",
            NormalizeStatus(pulse.Status),
            new SearchConsoleConnectionConfig
            {
                ClientId = clientId,
                LastValidatedAt = pulse.LastValidatedAt,
                Sites = pulse.Sites,
                ImportSource = ImportSource,
            }).ConfigureAwait(false);
    }

    private static PulseWebAnalyticsImport? ReadPulseWebAnalytics(string pulseDbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = pulseDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();

        var row = connection.QueryFirstOrDefault<PulseConnectionRow>(
            "SELECT status AS Status, config AS Config FROM connections WHERE id = 'wa'");
        var configJson = row?.Config;
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        var config = JsonSerializer.Deserialize<PulseWebAnalyticsConfig>(configJson, JsonOptions);
        if (config is null || string.IsNullOrWhiteSpace(config.AccountId))
        {
            return null;
        }

        var sites = connection.Query<PulseWebAnalyticsSite>(
            """
            SELECT domain AS Domain, site_tag AS SiteTag, discovered_at AS DiscoveredAt
            FROM web_analytics_sites
            WHERE domain IS NOT NULL AND site_tag IS NOT NULL
            ORDER BY domain
            """).AsList();

        return new PulseWebAnalyticsImport
        {
            AccountId = config.AccountId.Trim(),
            ApiToken = config.ApiToken?.Trim() ?? "",
            Status = row?.Status ?? "configured",
            LastValidatedAt = config.LastValidatedAt,
            Sites = sites,
        };
    }

    private static List<PulseCloudflareImport> ReadPulseCloudflareConnections(string pulseDbPath)
    {
        using var connection = OpenPulseConnection(pulseDbPath);
        var rows = connection.Query<PulseConnectionRow>(
            """
            SELECT status AS Status, config AS Config
            FROM connections
            WHERE source = 'cloudflare' AND config IS NOT NULL
            ORDER BY id
            """).AsList();

        var result = new List<PulseCloudflareImport>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Config))
            {
                continue;
            }

            var config = JsonSerializer.Deserialize<PulseCloudflareConfig>(row.Config, JsonOptions);
            if (config is null)
            {
                continue;
            }

            result.Add(new PulseCloudflareImport
            {
                Domain = config.Domain,
                ZoneId = config.ZoneId,
                ApiToken = config.ApiToken,
                LastValidatedAt = config.LastValidatedAt,
                Status = row.Status,
            });
        }
        return result;
    }

    private static PulseSearchConsoleImport? ReadPulseSearchConsole(string pulseDbPath)
    {
        using var connection = OpenPulseConnection(pulseDbPath);
        var row = connection.QueryFirstOrDefault<PulseConnectionRow>(
            "SELECT status AS Status, config AS Config FROM connections WHERE id = 'sc'");
        if (string.IsNullOrWhiteSpace(row?.Config))
        {
            return null;
        }

        var config = JsonSerializer.Deserialize<PulseSearchConsoleConfig>(row.Config, JsonOptions);
        if (config is null || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return null;
        }

        return new PulseSearchConsoleImport
        {
            ClientId = config.ClientId,
            ClientSecret = config.ClientSecret,
            RefreshToken = config.RefreshToken,
            LastValidatedAt = config.LastValidatedAt,
            Sites = config.Sites,
            Status = row.Status,
        };
    }

    private Task UpsertImportedConnectionAsync<TConfig>(string id, string source, string status, TConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO connections (id, source, status, config, last_sync)
                VALUES (@id, @source, @status, @json, NULL)
                ON CONFLICT(id) DO UPDATE SET
                    status = CASE
                        WHEN connections.status = 'connected' THEN connections.status
                        ELSE excluded.status
                    END,
                    config = excluded.config
                """,
                new { id, source, status, json });
        });
    }

    private static SqliteConnection OpenPulseConnection(string pulseDbPath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = pulseDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string GetPulseDatabasePath()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(roaming, "com.finnvek.pulse", "pulse.db");
    }

    private static string NormalizeCloudflareToken(string token)
    {
        token = token.Trim();
        const string bearerPrefix = "Bearer ";
        return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? token[bearerPrefix.Length..].Trim()
            : token;
    }

    private static string NormalizeStatus(string? status)
        => string.IsNullOrWhiteSpace(status) || status == "mock" ? "configured" : status.Trim();

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }
        return null;
    }

    private sealed class PulseConnectionRow
    {
        public string Status { get; set; } = "";
        public string? Config { get; set; }
    }

    private sealed class PulseCloudflareConfig
    {
        [JsonPropertyName("domain")]
        public string Domain { get; set; } = "";

        [JsonPropertyName("zone_id")]
        public string ZoneId { get; set; } = "";

        [JsonPropertyName("api_token")]
        public string ApiToken { get; set; } = "";

        [JsonPropertyName("last_validated_at")]
        public string? LastValidatedAt { get; set; }
    }

    private sealed class PulseCloudflareImport
    {
        public string Domain { get; set; } = "";
        public string ZoneId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string? LastValidatedAt { get; set; }
        public string Status { get; set; } = "";
    }

    private sealed class PulseSearchConsoleConfig
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = "";

        [JsonPropertyName("client_secret")]
        public string ClientSecret { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = "";

        [JsonPropertyName("last_validated_at")]
        public string? LastValidatedAt { get; set; }

        [JsonPropertyName("sites")]
        public List<string> Sites { get; set; } = new();
    }

    private sealed class PulseSearchConsoleImport
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string? LastValidatedAt { get; set; }
        public List<string> Sites { get; set; } = new();
        public string Status { get; set; } = "";
    }

    private sealed class PulseWebAnalyticsConfig
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; } = "";

        [JsonPropertyName("api_token")]
        public string ApiToken { get; set; } = "";

        [JsonPropertyName("last_validated_at")]
        public string? LastValidatedAt { get; set; }
    }

    private sealed class PulseWebAnalyticsImport
    {
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string Status { get; set; } = "";
        public string? LastValidatedAt { get; set; }
        public List<PulseWebAnalyticsSite> Sites { get; set; } = new();
    }

    private sealed class PulseWebAnalyticsSite
    {
        public string Domain { get; set; } = "";
        public string SiteTag { get; set; } = "";
        public string? DiscoveredAt { get; set; }
    }
}
