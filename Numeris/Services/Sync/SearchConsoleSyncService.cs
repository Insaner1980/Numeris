using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class SearchConsoleSyncService
{
    private readonly SqliteDatabase _db;
    private readonly SearchConsoleClient _client;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public SearchConsoleSyncService(SqliteDatabase db, SearchConsoleClient client, CredentialVault vault, ConnectionsRepository connectionsRepo)
    {
        _db = db;
        _client = client;
        _vault = vault;
        _connectionsRepo = connectionsRepo;
    }

    public async Task<SyncResult> SyncAsync(string clientId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var clientSecret = _vault.GetSearchConsoleClientSecret(clientId)
            ?? throw new InvalidOperationException("No client secret saved");
        var refreshToken = _vault.GetSearchConsoleRefreshToken(clientId)
            ?? throw new InvalidOperationException("Not authorized — connect Google account first");

        var accessToken = await _client.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken).ConfigureAwait(false);
        var sites = await _client.ListSitesAsync(accessToken).ConfigureAwait(false);

        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var startStr = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endStr = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var nowStr = _connectionsRepo.FormatNow();

        long records = 0;
        var domainsToSync = new[] { Domains.KnitTools, Domains.Finnvek };
        var pqEnd = endDate;
        var pqStart = endDate.AddDays(-27);
        var pqStartStr = pqStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pqEndStr = pqEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var domain in domainsToSync)
        {
            var property = SearchConsoleClient.PropertyForDomain(sites, domain);
            if (property is null) continue;

            foreach (var kind in new[] { SearchQueryKind.Daily, SearchQueryKind.Query, SearchQueryKind.Page, SearchQueryKind.Country, SearchQueryKind.Device })
            {
                var rows = await _client.QueryAsync(accessToken, property, startStr, endStr, kind, 5000).ConfigureAwait(false);
                await _db.WriteAsync(connection =>
                {
                    var kindStr = kind switch
                    {
                        SearchQueryKind.Daily => "daily",
                        SearchQueryKind.Query => "query",
                        SearchQueryKind.Page => "page",
                        SearchQueryKind.Country => "country",
                        SearchQueryKind.Device => "device",
                        _ => "query",
                    };

                    connection.Execute(
                        "DELETE FROM search_console WHERE site_url = @siteUrl AND kind = @kind AND date BETWEEN @startStr AND @endStr",
                        new { siteUrl = domain, kind = kindStr, startStr, endStr });

                    if (kind == SearchQueryKind.Device)
                    {
                        connection.Execute(
                            "DELETE FROM search_devices WHERE site_url = @siteUrl AND date BETWEEN @startStr AND @endStr",
                            new { siteUrl = domain, startStr, endStr });
                    }

                    foreach (var row in rows)
                    {
                        connection.Execute(
                            """
                            INSERT INTO search_console
                            (site_url, date, kind, query, page, clicks, impressions, ctr, position, country, fetched_at)
                            VALUES (@siteUrl, @date, @kind, @query, @page, @clicks, @impressions, @ctr, @position, @country, @fetchedAt)
                            """,
                            new
                            {
                                siteUrl = domain,
                                date = row.Date,
                                kind = kindStr,
                                query = row.Query,
                                page = row.Page,
                                clicks = row.Clicks,
                                impressions = row.Impressions,
                                ctr = row.Ctr,
                                position = row.Position,
                                country = row.Country,
                                fetchedAt = nowStr,
                            });
                        records++;

                        if (kind == SearchQueryKind.Device && !string.IsNullOrEmpty(row.Device))
                        {
                            connection.Execute(
                                """
                                INSERT INTO search_devices (site_url, date, device, clicks, impressions, ctr, position)
                                VALUES (@siteUrl, @date, @device, @clicks, @impressions, @ctr, @position)
                                ON CONFLICT(site_url, date, device) DO UPDATE SET
                                    clicks = excluded.clicks,
                                    impressions = excluded.impressions,
                                    ctr = excluded.ctr,
                                    position = excluded.position
                                """,
                                new
                                {
                                    siteUrl = domain,
                                    date = row.Date,
                                    device = row.Device,
                                    clicks = row.Clicks,
                                    impressions = row.Impressions,
                                    ctr = row.Ctr,
                                    position = row.Position,
                                });
                        }
                    }
                }).ConfigureAwait(false);
            }

            // PageQuery snapshot for the latest 28-day window
            var pqRows = await _client.QueryAsync(accessToken, property, pqStartStr, pqEndStr, SearchQueryKind.PageQuery, 5000).ConfigureAwait(false);
            await _db.WriteAsync(connection =>
            {
                connection.Execute(
                    "DELETE FROM search_page_queries WHERE site_url = @siteUrl AND period_start = @pqStartStr AND period_end = @pqEndStr",
                    new { siteUrl = domain, pqStartStr, pqEndStr });
                foreach (var row in pqRows.Where(r => !string.IsNullOrEmpty(r.Page)))
                {
                    connection.Execute(
                        """
                        INSERT OR REPLACE INTO search_page_queries
                        (site_url, period_start, period_end, page, query, clicks, impressions, ctr, position)
                        VALUES (@siteUrl, @periodStart, @periodEnd, @page, @query, @clicks, @impressions, @ctr, @position)
                        """,
                        new
                        {
                            siteUrl = domain,
                            periodStart = pqStartStr,
                            periodEnd = pqEndStr,
                            page = row.Page!,
                            query = row.Query,
                            clicks = row.Clicks,
                            impressions = row.Impressions,
                            ctr = row.Ctr,
                            position = row.Position,
                        });
                    records++;
                }
            }).ConfigureAwait(false);
        }

        await _connectionsRepo.UpdateSearchConsoleLastSyncAsync(clientId, nowStr, "connected").ConfigureAwait(false);

        return new SyncResult { Domain = "search_console", DaysSynced = days, RecordsUpserted = records };
    }
}
