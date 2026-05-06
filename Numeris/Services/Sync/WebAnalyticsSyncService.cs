using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class WebAnalyticsSyncService
{
    private readonly SqliteDatabase _db;
    private readonly CloudflareRumClient _client;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public WebAnalyticsSyncService(SqliteDatabase db, CloudflareRumClient client, CredentialVault vault, ConnectionsRepository connectionsRepo)
    {
        _db = db;
        _client = client;
        _vault = vault;
        _connectionsRepo = connectionsRepo;
    }

    public async Task<List<WebAnalyticsSite>> DiscoverSitesAsync(string accountId)
    {
        var token = _vault.GetWebAnalyticsToken(accountId)
            ?? throw new InvalidOperationException("No API token saved for this account");
        var sites = await _client.ListSitesAsync(token, accountId).ConfigureAwait(false);

        await SaveSitesAsync(sites).ConfigureAwait(false);
        return sites;
    }

    public Task<List<WebAnalyticsSite>> ListSavedSitesAsync()
    {
        return _db.ReadAsync(connection =>
            connection.Query<WebAnalyticsSite>(
                """
                SELECT domain AS Host, site_tag AS SiteTag
                FROM web_analytics_sites
                ORDER BY domain
                """).AsList()
        );
    }

    public async Task<List<WebAnalyticsSite>> AddManualSiteAsync(string domain, string siteTag)
    {
        domain = domain.Trim().ToLowerInvariant();
        siteTag = siteTag.Trim();
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Domain is required");
        }
        if (string.IsNullOrWhiteSpace(siteTag))
        {
            throw new InvalidOperationException("Site tag is required");
        }

        await SaveSitesAsync(new List<WebAnalyticsSite>
        {
            new() { Host = domain, SiteTag = siteTag },
        }).ConfigureAwait(false);
        return await ListSavedSitesAsync().ConfigureAwait(false);
    }

    public async Task<List<WebAnalyticsSite>> DeleteSiteAsync(string domain)
    {
        domain = domain.Trim().ToLowerInvariant();
        await _db.WriteAsync(connection =>
        {
            connection.Execute("DELETE FROM web_analytics_sites WHERE domain = @domain", new { domain });
        }).ConfigureAwait(false);
        return await ListSavedSitesAsync().ConfigureAwait(false);
    }

    private Task SaveSitesAsync(IReadOnlyCollection<WebAnalyticsSite> sites)
    {
        var nowStr = _connectionsRepo.FormatNow();
        return _db.WriteAsync(connection =>
        {
            foreach (var site in sites)
            {
                connection.Execute(
                    """
                    INSERT INTO web_analytics_sites (domain, site_tag, discovered_at)
                    VALUES (@domain, @siteTag, @discoveredAt)
                    ON CONFLICT(domain) DO UPDATE SET site_tag = excluded.site_tag
                    """,
                    new { domain = site.Host, siteTag = site.SiteTag, discoveredAt = nowStr });
            }
        });
    }

    public async Task<SyncResult> SyncAccountAsync(string accountId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var token = _vault.GetWebAnalyticsToken(accountId)
            ?? throw new InvalidOperationException("No API token saved for this account");

        var sites = await _db.ReadAsync(connection =>
            connection.Query<(string Domain, string SiteTag)>(
                "SELECT domain, site_tag AS SiteTag FROM web_analytics_sites").AsList()
        ).ConfigureAwait(false);

        if (sites.Count == 0)
        {
            throw new InvalidOperationException("No Web Analytics site tags saved. Add a manual site tag mapping, then press Sync.");
        }

        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var sinceDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var untilDate = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sinceIso = startDate.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var untilIso = endDate.ToDateTime(new TimeOnly(23, 59, 59)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var nowStr = _connectionsRepo.FormatNow();

        long records = 0;
        long days_total = 0;

        foreach (var site in sites)
        {
            var rollup = await _client.FetchRollupAsync(token, accountId, site.SiteTag, sinceIso, untilIso, sinceDate, untilDate).ConfigureAwait(false);
            await _db.WriteAsync(connection =>
            {
                foreach (var d in rollup.Daily)
                {
                    connection.Execute(
                        """
                        INSERT INTO web_analytics_daily (domain, date, visits, page_views, fetched_at)
                        VALUES (@domain, @date, @visits, @pageViews, @fetchedAt)
                        ON CONFLICT(domain, date) DO UPDATE SET
                            visits = excluded.visits,
                            page_views = excluded.page_views,
                            fetched_at = excluded.fetched_at
                        """,
                        new { domain = site.Domain, date = d.Date, visits = d.Visits, pageViews = d.PageViews, fetchedAt = nowStr });
                    records++;
                    days_total++;
                }

                connection.Execute("DELETE FROM web_analytics_referrers WHERE domain = @domain AND date = @date",
                    new { domain = site.Domain, date = untilDate });
                foreach (var r in rollup.Referrers)
                {
                    connection.Execute(
                        """
                        INSERT INTO web_analytics_referrers (domain, date, referrer, visits)
                        VALUES (@domain, @date, @referrer, @visits)
                        ON CONFLICT(domain, date, referrer) DO UPDATE SET visits = excluded.visits
                        """,
                        new { domain = site.Domain, date = untilDate, referrer = r.Key, visits = r.Visits });
                    records++;
                }

                connection.Execute("DELETE FROM web_analytics_pages WHERE domain = @domain AND date = @date",
                    new { domain = site.Domain, date = untilDate });
                foreach (var p in rollup.Pages)
                {
                    connection.Execute(
                        """
                        INSERT INTO web_analytics_pages (domain, date, path, page_views)
                        VALUES (@domain, @date, @path, @pageViews)
                        ON CONFLICT(domain, date, path) DO UPDATE SET page_views = excluded.page_views
                        """,
                        new { domain = site.Domain, date = untilDate, path = p.Path, pageViews = p.PageViews });
                    records++;
                }

                connection.Execute("DELETE FROM web_analytics_countries WHERE domain = @domain AND date = @date",
                    new { domain = site.Domain, date = untilDate });
                foreach (var c in rollup.Countries)
                {
                    connection.Execute(
                        """
                        INSERT INTO web_analytics_countries (domain, date, country, visits)
                        VALUES (@domain, @date, @country, @visits)
                        ON CONFLICT(domain, date, country) DO UPDATE SET visits = excluded.visits
                        """,
                        new { domain = site.Domain, date = untilDate, country = c.Key, visits = c.Visits });
                    records++;
                }
            }).ConfigureAwait(false);
        }

        await _connectionsRepo.UpdateWebAnalyticsLastSyncAsync(accountId, nowStr, "connected").ConfigureAwait(false);

        return new SyncResult
        {
            Domain = $"{sites.Count} sites",
            DaysSynced = days_total,
            RecordsUpserted = records,
        };
    }

    public async Task<ConnectionTestResult> TestAccountAsync(string accountId)
    {
        accountId = accountId.Trim();
        var token = _vault.GetWebAnalyticsToken(accountId);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ConnectionTestResult { Ok = false, Message = "No Account Analytics token saved for this account" };
        }

        var sites = await _db.ReadAsync(connection =>
            connection.Query<(string Domain, string SiteTag)>(
                "SELECT domain, site_tag AS SiteTag FROM web_analytics_sites ORDER BY domain LIMIT 1").AsList()
        ).ConfigureAwait(false);
        if (sites.Count == 0)
        {
            return new ConnectionTestResult
            {
                Ok = false,
                Message = "No Web Analytics site tag saved. Add a manual mapping; Discover sites is optional.",
            };
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var sinceDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var untilDate = sinceDate;
        var sinceIso = today.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var untilIso = today.ToDateTime(new TimeOnly(23, 59, 59)).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        try
        {
            var site = sites[0];
            var rollup = await _client.FetchRollupAsync(token, accountId, site.SiteTag, sinceIso, untilIso, sinceDate, untilDate).ConfigureAwait(false);
            return new ConnectionTestResult
            {
                Ok = true,
                Message = rollup.Daily.Count == 0
                    ? $"Web Analytics token works for {site.Domain}, but today's dataset is empty"
                    : $"Web Analytics token works for {site.Domain}; GraphQL returned {rollup.Daily.Count} day row(s)",
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ex.Message };
        }
    }
}
