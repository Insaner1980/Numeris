using System;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class CloudflareSyncService
{
    private readonly SqliteDatabase _db;
    private readonly CloudflareGraphqlClient _client;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public CloudflareSyncService(SqliteDatabase db, CloudflareGraphqlClient client, CredentialVault vault, ConnectionsRepository connectionsRepo)
    {
        _db = db;
        _client = client;
        _vault = vault;
        _connectionsRepo = connectionsRepo;
    }

    public async Task<SyncResult> SyncDomainAsync(string domain, string zoneId, int days)
    {
        days = Math.Clamp(days, 1, 3650);
        var apiToken = _vault.GetCloudflareToken(domain)
            ?? throw new InvalidOperationException($"No API token saved for {domain} — re-add it on Sources page");

        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var startStr = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endStr = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var traffic = await _client.FetchDailyTrafficAsync(apiToken, zoneId, startStr, endStr).ConfigureAwait(false);

        var nowStr = _connectionsRepo.FormatNow();
        var records = await _db.WriteAsync(connection =>
        {
            long count = 0;
            foreach (var row in traffic.Daily)
            {
                connection.Execute(
                    """
                    INSERT INTO cloudflare_traffic
                    (domain, date, pageviews, unique_visitors, requests, cached_requests,
                     cached_bytes, total_bytes, threats, top_country, top_path, fetched_at)
                    VALUES (@domain, @date, @pageviews, @uniqueVisitors, @requests, @cachedRequests,
                            @cachedBytes, @totalBytes, @threats, NULL, NULL, @fetchedAt)
                    ON CONFLICT(domain, date) DO UPDATE SET
                        pageviews = excluded.pageviews,
                        unique_visitors = excluded.unique_visitors,
                        requests = excluded.requests,
                        cached_requests = excluded.cached_requests,
                        cached_bytes = excluded.cached_bytes,
                        total_bytes = excluded.total_bytes,
                        threats = excluded.threats,
                        fetched_at = excluded.fetched_at
                    """,
                    new
                    {
                        domain,
                        row.Date,
                        row.Pageviews,
                        uniqueVisitors = row.UniqueVisitors,
                        row.Requests,
                        cachedRequests = row.CachedRequests,
                        cachedBytes = row.CachedBytes,
                        totalBytes = row.TotalBytes,
                        row.Threats,
                        fetchedAt = nowStr,
                    });
                count++;
            }
            foreach (var s in traffic.StatusCodes)
            {
                connection.Execute(
                    """
                    INSERT INTO cloudflare_status_codes (domain, date, status_code, requests)
                    VALUES (@domain, @date, @statusCode, @requests)
                    ON CONFLICT(domain, date, status_code) DO UPDATE SET requests = excluded.requests
                    """,
                    new { domain, s.Date, statusCode = s.StatusCode, s.Requests });
                count++;
            }
            return count;
        }).ConfigureAwait(false);

        await _connectionsRepo.UpdateCloudflareLastSyncAsync(domain, nowStr, "connected").ConfigureAwait(false);

        return new SyncResult
        {
            Domain = domain,
            DaysSynced = traffic.Daily.Count,
            RecordsUpserted = records,
        };
    }
}
