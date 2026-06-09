using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Migration;

public sealed class LegacyDataMigrationService
{
    private static readonly LegacyAppSource ImportSource = new()
    {
        ConfigTag = "legacy-numeris-tauri",
        CredentialService = "Numeris",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SqliteDatabase _db;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public LegacyDataMigrationService(SqliteDatabase db, CredentialVault vault, ConnectionsRepository connectionsRepo)
    {
        _db = db;
        _vault = vault;
        _connectionsRepo = connectionsRepo;
    }

    public async Task ImportAllAsync()
    {
        var sourceDbPath = GetSourceDatabasePath();
        if (!File.Exists(sourceDbPath))
        {
            return;
        }

        await ImportWebAnalyticsAsync(sourceDbPath).ConfigureAwait(false);
        await ImportCloudflareAsync(sourceDbPath).ConfigureAwait(false);
        await ImportSearchConsoleAsync(sourceDbPath).ConfigureAwait(false);
    }

    public async Task ImportWebAnalyticsAsync()
    {
        var sourceDbPath = GetSourceDatabasePath();
        if (!File.Exists(sourceDbPath))
        {
            return;
        }

        await ImportWebAnalyticsAsync(sourceDbPath).ConfigureAwait(false);
    }

    private async Task ImportWebAnalyticsAsync(string sourceDbPath)
    {
        var source = ReadWebAnalyticsSource(sourceDbPath);
        if (source is null || string.IsNullOrWhiteSpace(source.AccountId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_vault.GetWebAnalyticsToken(source.AccountId)))
        {
            var apiToken = FirstNonBlank(
                LegacyCredentialReader.ReadKeyringPassword(
                    $"web_analytics:{source.AccountId}",
                    ImportSource.CredentialService),
                source.ApiToken);
            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                _vault.SetWebAnalyticsToken(source.AccountId, NormalizeCloudflareToken(apiToken));
            }
        }

        await UpsertImportedConnectionAsync(
            "wa",
            "web_analytics",
            NormalizeStatus(source.Status),
            new WebAnalyticsConnectionConfig
            {
                AccountId = source.AccountId,
                LastValidatedAt = source.LastValidatedAt ?? _connectionsRepo.FormatNow(),
                ImportSource = ImportSource.ConfigTag,
            }).ConfigureAwait(false);

        if (source.Sites.Count == 0)
        {
            return;
        }

        var nowStr = _connectionsRepo.FormatNow();
        await _db.WriteAsync(connection =>
        {
            foreach (var site in source.Sites)
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

    private async Task ImportCloudflareAsync(string sourceDbPath)
    {
        foreach (var source in ReadCloudflareSourceConnections(sourceDbPath))
        {
            if (string.IsNullOrWhiteSpace(source.Domain) || string.IsNullOrWhiteSpace(source.ZoneId))
            {
                continue;
            }

            var domain = source.Domain.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(_vault.GetCloudflareToken(domain)))
            {
                var apiToken = FirstNonBlank(
                    LegacyCredentialReader.ReadKeyringPassword(
                        $"cloudflare:{domain}",
                        ImportSource.CredentialService),
                    source.ApiToken);
                if (!string.IsNullOrWhiteSpace(apiToken))
                {
                    _vault.SetCloudflareToken(domain, NormalizeCloudflareToken(apiToken));
                }
            }

            await UpsertImportedConnectionAsync(
                $"cloudflare:{domain}",
                "cloudflare",
                NormalizeStatus(source.Status),
                new CloudflareConnectionConfig
                {
                    Domain = domain,
                    ZoneId = source.ZoneId.Trim(),
                    LastValidatedAt = source.LastValidatedAt,
                    ImportSource = ImportSource.ConfigTag,
                }).ConfigureAwait(false);
        }
    }

    private async Task ImportSearchConsoleAsync(string sourceDbPath)
    {
        var source = ReadSearchConsoleSource(sourceDbPath);
        if (source is null || string.IsNullOrWhiteSpace(source.ClientId))
        {
            return;
        }

        var clientId = source.ClientId.Trim();
        if (string.IsNullOrWhiteSpace(_vault.GetSearchConsoleClientSecret(clientId)))
        {
            var clientSecret = FirstNonBlank(
                LegacyCredentialReader.ReadKeyringPassword(
                    $"search_console:client_secret:{clientId}",
                    ImportSource.CredentialService),
                source.ClientSecret);
            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                _vault.SetSearchConsoleClientSecret(clientId, clientSecret);
            }
        }
        if (string.IsNullOrWhiteSpace(_vault.GetSearchConsoleRefreshToken(clientId)))
        {
            var refreshToken = FirstNonBlank(
                LegacyCredentialReader.ReadKeyringPassword(
                    $"search_console:refresh_token:{clientId}",
                    ImportSource.CredentialService),
                source.RefreshToken);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                _vault.SetSearchConsoleRefreshToken(clientId, refreshToken);
            }
        }

        await UpsertImportedConnectionAsync(
            "sc",
            "search_console",
            NormalizeStatus(source.Status),
            new SearchConsoleConnectionConfig
            {
                ClientId = clientId,
                LastValidatedAt = source.LastValidatedAt,
                Sites = source.Sites,
                ImportSource = ImportSource.ConfigTag,
            }).ConfigureAwait(false);
    }

    private static LegacyWebAnalyticsImport? ReadWebAnalyticsSource(string sourceDbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourceDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();

        var row = connection.QueryFirstOrDefault<LegacyConnectionRow>(
            "SELECT status AS Status, config AS Config FROM connections WHERE id = 'wa'");
        var configJson = row?.Config;
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        var config = JsonSerializer.Deserialize<LegacyWebAnalyticsConfig>(configJson, JsonOptions);
        if (config is null || string.IsNullOrWhiteSpace(config.AccountId))
        {
            return null;
        }

        var sites = connection.Query<LegacyWebAnalyticsSite>(
            """
            SELECT domain AS Domain, site_tag AS SiteTag, discovered_at AS DiscoveredAt
            FROM web_analytics_sites
            WHERE domain IS NOT NULL AND site_tag IS NOT NULL
            ORDER BY domain
            """).AsList();

        return new LegacyWebAnalyticsImport
        {
            AccountId = config.AccountId.Trim(),
            ApiToken = config.ApiToken?.Trim() ?? "",
            Status = row?.Status ?? "configured",
            LastValidatedAt = config.LastValidatedAt,
            Sites = sites,
        };
    }

    private static List<LegacyCloudflareImport> ReadCloudflareSourceConnections(string sourceDbPath)
    {
        using var connection = OpenSourceConnection(sourceDbPath);
        var rows = connection.Query<LegacyConnectionRow>(
            """
            SELECT status AS Status, config AS Config
            FROM connections
            WHERE source = 'cloudflare' AND config IS NOT NULL
            ORDER BY id
            """).AsList();

        var result = new List<LegacyCloudflareImport>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Config))
            {
                continue;
            }

            var config = JsonSerializer.Deserialize<LegacyCloudflareConfig>(row.Config, JsonOptions);
            if (config is null)
            {
                continue;
            }

            result.Add(new LegacyCloudflareImport
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

    private static LegacySearchConsoleImport? ReadSearchConsoleSource(string sourceDbPath)
    {
        using var connection = OpenSourceConnection(sourceDbPath);
        var row = connection.QueryFirstOrDefault<LegacyConnectionRow>(
            "SELECT status AS Status, config AS Config FROM connections WHERE id = 'sc'");
        if (string.IsNullOrWhiteSpace(row?.Config))
        {
            return null;
        }

        var config = JsonSerializer.Deserialize<LegacySearchConsoleConfig>(row.Config, JsonOptions);
        if (config is null || string.IsNullOrWhiteSpace(config.ClientId))
        {
            return null;
        }

        return new LegacySearchConsoleImport
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

    private static SqliteConnection OpenSourceConnection(string sourceDbPath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourceDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string GetSourceDatabasePath()
        => AppPaths.LegacyNumerisDatabasePath;

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

    private sealed class LegacyAppSource
    {
        public string ConfigTag { get; init; } = "";
        public string CredentialService { get; init; } = "";
    }

    private sealed class LegacyConnectionRow
    {
        public string Status { get; set; } = "";
        public string? Config { get; set; }
    }

    private sealed class LegacyCloudflareConfig
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

    private sealed class LegacyCloudflareImport
    {
        public string Domain { get; set; } = "";
        public string ZoneId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string? LastValidatedAt { get; set; }
        public string Status { get; set; } = "";
    }

    private sealed class LegacySearchConsoleConfig
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

    private sealed class LegacySearchConsoleImport
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string? LastValidatedAt { get; set; }
        public List<string> Sites { get; set; } = new();
        public string Status { get; set; } = "";
    }

    private sealed class LegacyWebAnalyticsConfig
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; } = "";

        [JsonPropertyName("api_token")]
        public string ApiToken { get; set; } = "";

        [JsonPropertyName("last_validated_at")]
        public string? LastValidatedAt { get; set; }
    }

    private sealed class LegacyWebAnalyticsImport
    {
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public string Status { get; set; } = "";
        public string? LastValidatedAt { get; set; }
        public List<LegacyWebAnalyticsSite> Sites { get; set; } = new();
    }

    private sealed class LegacyWebAnalyticsSite
    {
        public string Domain { get; set; } = "";
        public string SiteTag { get; set; } = "";
        public string? DiscoveredAt { get; set; }
    }
}
