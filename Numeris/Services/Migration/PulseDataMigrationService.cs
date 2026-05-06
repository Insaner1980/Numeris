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

    public async Task ImportWebAnalyticsAsync()
    {
        var pulseDbPath = GetPulseDatabasePath();
        if (!File.Exists(pulseDbPath))
        {
            return;
        }

        var pulse = ReadPulseWebAnalytics(pulseDbPath);
        if (pulse is null || string.IsNullOrWhiteSpace(pulse.AccountId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(pulse.ApiToken))
        {
            _vault.SetWebAnalyticsToken(pulse.AccountId, NormalizeCloudflareToken(pulse.ApiToken));
        }

        await _connectionsRepo.UpsertWebAnalyticsAsync(
            new WebAnalyticsConnectionConfig
            {
                AccountId = pulse.AccountId,
                LastValidatedAt = _connectionsRepo.FormatNow(),
                ImportSource = ImportSource,
            },
            "configured").ConfigureAwait(false);

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

    private static PulseWebAnalyticsImport? ReadPulseWebAnalytics(string pulseDbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = pulseDbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();

        var configJson = connection.QueryFirstOrDefault<string?>(
            "SELECT config FROM connections WHERE id = 'wa'");
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
            Sites = sites,
        };
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

    private sealed class PulseWebAnalyticsConfig
    {
        [JsonPropertyName("account_id")]
        public string AccountId { get; set; } = "";

        [JsonPropertyName("api_token")]
        public string ApiToken { get; set; } = "";
    }

    private sealed class PulseWebAnalyticsImport
    {
        public string AccountId { get; set; } = "";
        public string ApiToken { get; set; } = "";
        public List<PulseWebAnalyticsSite> Sites { get; set; } = new();
    }

    private sealed class PulseWebAnalyticsSite
    {
        public string Domain { get; set; } = "";
        public string SiteTag { get; set; } = "";
        public string? DiscoveredAt { get; set; }
    }
}
