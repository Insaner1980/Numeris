using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class WebAnalyticsSyncService
{
    private readonly CloudflareRumClient _client;
    private readonly WebAnalyticsRepository _webAnalyticsRepo;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public WebAnalyticsSyncService(CloudflareRumClient client, WebAnalyticsRepository webAnalyticsRepo, CredentialVault vault, ConnectionsRepository connectionsRepo)
    {
        _client = client;
        _webAnalyticsRepo = webAnalyticsRepo;
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
        => _webAnalyticsRepo.ListSitesAsync();

    public async Task<List<WebAnalyticsSite>> AddManualSiteAsync(string domain, string siteTag)
    {
        domain = SiteIdentity.NormalizeDomain(domain);
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
        domain = SiteIdentity.NormalizeDomain(domain);
        await _webAnalyticsRepo.DeleteSiteAsync(domain).ConfigureAwait(false);
        return await ListSavedSitesAsync().ConfigureAwait(false);
    }

    private Task SaveSitesAsync(IReadOnlyCollection<WebAnalyticsSite> sites)
    {
        var nowStr = _connectionsRepo.FormatNow();
        return _webAnalyticsRepo.SaveSitesAsync(sites, nowStr);
    }

    public async Task<SyncResult> SyncAccountAsync(string accountId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var token = _vault.GetWebAnalyticsToken(accountId)
            ?? throw new InvalidOperationException("No API token saved for this account");

        var sites = await _webAnalyticsRepo.ListSiteTagsAsync().ConfigureAwait(false);

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
            records += await _webAnalyticsRepo.UpsertRollupAsync(site.Domain, untilDate, rollup, nowStr).ConfigureAwait(false);
            days_total += rollup.Daily.Count;
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

        var sites = await _webAnalyticsRepo.ListSiteTagsAsync().ConfigureAwait(false);
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
            return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
        }
    }
}
