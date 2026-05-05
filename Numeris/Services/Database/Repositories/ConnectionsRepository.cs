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
}
