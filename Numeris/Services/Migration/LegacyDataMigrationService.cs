using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private const string ImportedConnectionSql = """
        INSERT INTO connections (id, source, status, config, last_sync)
        VALUES (@id, @source, @status, @json, NULL)
        ON CONFLICT(id) DO UPDATE SET
            status = CASE
                WHEN connections.status = 'connected' THEN connections.status
                ELSE excluded.status
            END,
            config = excluded.config
        """;

    private readonly SqliteDatabase _db;
    private readonly CredentialVault _vault;

    public LegacyDataMigrationService(SqliteDatabase db, CredentialVault vault)
    {
        _db = db;
        _vault = vault;
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

        var nowStr = ConnectionsRepository.FormatNow();
        await _db.WriteTransactionAsync((connection, transaction) =>
        {
            connection.Execute(ImportedConnectionSql, new
            {
                id = "wa",
                source = "web_analytics",
                status = NormalizeStatus(source.Status),
                json = JsonSerializer.Serialize(new WebAnalyticsConnectionConfig
                {
                    AccountId = source.AccountId,
                    LastValidatedAt = source.LastValidatedAt ?? nowStr,
                    ImportSource = ImportSource.ConfigTag,
                }, JsonOptions),
            }, transaction);
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
                        domain = SiteIdentity.NormalizeDomain(site.Domain),
                        siteTag = site.SiteTag,
                        discoveredAt = string.IsNullOrWhiteSpace(site.DiscoveredAt) ? nowStr : site.DiscoveredAt,
                    }, transaction);
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

            var legacyDomain = source.Domain.Trim().ToLowerInvariant();
            var domain = SiteIdentity.NormalizeDomain(legacyDomain);
            if (string.IsNullOrWhiteSpace(_vault.GetCloudflareToken(domain)))
            {
                var apiToken = FirstNonBlank(
                    LegacyCredentialReader.ReadKeyringPassword(
                        $"cloudflare:{legacyDomain}",
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

        if (!HasColumns(connection, "connections", "id", "status", "config")) return null;

        var row = connection.QueryFirstOrDefault<LegacyConnectionRow>(
            "SELECT status AS Status, config AS Config FROM connections WHERE id = 'wa'");
        var configJson = row?.Config;
        if (row is null || string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        var config = ReadLegacyConfig<LegacyWebAnalyticsConfig>(configJson);
        if (config is null || string.IsNullOrWhiteSpace(config.AccountId))
        {
            return null;
        }

        var sites = HasColumns(connection, "web_analytics_sites", "domain", "site_tag", "discovered_at")
            ? connection.Query<LegacyWebAnalyticsSite>(
            """
            SELECT domain AS Domain, site_tag AS SiteTag, discovered_at AS DiscoveredAt
            FROM web_analytics_sites
            WHERE domain IS NOT NULL AND site_tag IS NOT NULL
            ORDER BY domain
            """).AsList()
            : new List<LegacyWebAnalyticsSite>();

        return new LegacyWebAnalyticsImport
        {
            AccountId = config.AccountId.Trim(),
            ApiToken = config.ApiToken?.Trim() ?? "",
            Status = row.Status ?? "configured",
            LastValidatedAt = config.LastValidatedAt,
            Sites = sites,
        };
    }

    private static List<LegacyCloudflareImport> ReadCloudflareSourceConnections(string sourceDbPath)
    {
        using var connection = OpenSourceConnection(sourceDbPath);
        if (!HasColumns(connection, "connections", "id", "source", "status", "config")) return new();
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

            var config = ReadLegacyConfig<LegacyCloudflareConfig>(row.Config);
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
        if (!HasColumns(connection, "connections", "id", "status", "config")) return null;
        var row = connection.QueryFirstOrDefault<LegacyConnectionRow>(
            "SELECT status AS Status, config AS Config FROM connections WHERE id = 'sc'");
        if (string.IsNullOrWhiteSpace(row?.Config))
        {
            return null;
        }

        var config = ReadLegacyConfig<LegacySearchConsoleConfig>(row.Config);
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
            Sites = config.Sites ?? new(),
            Status = row.Status,
        };
    }

    private Task UpsertImportedConnectionAsync<TConfig>(string id, string source, string status, TConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        return _db.WriteAsync(connection =>
        {
            connection.Execute(ImportedConnectionSql, new { id, source, status, json });
        });
    }

    private static bool HasColumns(SqliteConnection connection, string table, params string[] required)
    {
        var columns = connection.Query<string>("SELECT name FROM pragma_table_info(@table)", new { table })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return required.All(columns.Contains);
    }

    private static T? ReadLegacyConfig<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
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
        => string.IsNullOrWhiteSpace(status) || string.Equals(status.Trim(), "mock", StringComparison.OrdinalIgnoreCase)
            ? "configured" : status.Trim();

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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
