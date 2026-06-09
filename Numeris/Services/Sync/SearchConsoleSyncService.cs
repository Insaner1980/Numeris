using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class SearchConsoleSyncService
{
    private readonly SearchConsoleClient _client;
    private readonly GoogleOAuthClient _oauthClient;
    private readonly SearchConsoleRepository _searchConsoleRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public SearchConsoleSyncService(
        SearchConsoleClient client,
        GoogleOAuthClient oauthClient,
        SearchConsoleRepository searchConsoleRepo,
        SitemapRepository sitemapRepo,
        CredentialVault vault,
        ConnectionsRepository connectionsRepo)
    {
        _client = client;
        _oauthClient = oauthClient;
        _searchConsoleRepo = searchConsoleRepo;
        _sitemapRepo = sitemapRepo;
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

        var accessToken = await _oauthClient.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken).ConfigureAwait(false);
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
                records += await _searchConsoleRepo
                    .ReplaceSearchAnalyticsRowsAsync(domain, kind, rows, startStr, endStr, nowStr)
                    .ConfigureAwait(false);
            }

            var pqRows = await _client.QueryAsync(accessToken, property, pqStartStr, pqEndStr, SearchQueryKind.PageQuery, 5000).ConfigureAwait(false);
            records += await _searchConsoleRepo.ReplacePageQueryRowsAsync(domain, pqStartStr, pqEndStr, pqRows).ConfigureAwait(false);
        }

        await _connectionsRepo.UpdateSearchConsoleLastSyncAsync(clientId, nowStr, "connected").ConfigureAwait(false);

        return new SyncResult { Domain = "search_console", DaysSynced = days, RecordsUpserted = records };
    }

    public async Task<SyncResult?> SyncConfiguredAsync(int days)
    {
        var connection = await _connectionsRepo.GetSearchConsoleAsync().ConfigureAwait(false);
        if (connection is null || string.IsNullOrWhiteSpace(connection.ClientId) || !connection.HasRefreshToken)
        {
            return null;
        }

        return await SyncAsync(connection.ClientId, days).ConfigureAwait(false);
    }

    public async Task<IndexingInspectionResult> InspectSitemapUrlsAsync(
        string domain,
        IProgress<IndexingInspectionResult>? progress = null)
    {
        var connection = await _connectionsRepo.GetSearchConsoleAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Search Console is not configured");
        if (string.IsNullOrWhiteSpace(connection.ClientId))
        {
            throw new InvalidOperationException("Search Console client ID is missing");
        }

        var clientSecret = _vault.GetSearchConsoleClientSecret(connection.ClientId)
            ?? throw new InvalidOperationException("No client secret saved");
        var refreshToken = _vault.GetSearchConsoleRefreshToken(connection.ClientId)
            ?? throw new InvalidOperationException("Not authorized — connect Google account first");

        var accessToken = await _oauthClient.RefreshAccessTokenAsync(connection.ClientId, clientSecret, refreshToken).ConfigureAwait(false);
        var sites = await _client.ListSitesAsync(accessToken).ConfigureAwait(false);
        var property = SearchConsoleClient.PropertyForDomain(sites, domain)
            ?? throw new InvalidOperationException($"Google Search Console property was not found for {domain}");

        var urls = (await _sitemapRepo.ListUrlsAsync(domain).ConfigureAwait(false))
            .FindAll(url => url.RemovedAt is null);
        if (urls.Count == 0)
        {
            throw new InvalidOperationException("No sitemap URLs found. Refresh sitemap first.");
        }

        var result = new IndexingInspectionResult { Domain = domain, TotalUrls = urls.Count };
        var consecutiveErrors = 0;
        foreach (var url in urls)
        {
            try
            {
                var inspection = await _client.InspectUrlAsync(accessToken, property, url.Url).ConfigureAwait(false);
                await _sitemapRepo.UpdateInspectionAsync(domain, inspection).ConfigureAwait(false);
                result.UrlsChecked++;
                consecutiveErrors = 0;
                if (string.Equals(inspection.Verdict, "PASS", StringComparison.OrdinalIgnoreCase)
                    || (inspection.CoverageState?.StartsWith("Indexed", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    result.Indexed++;
                }
                else
                {
                    result.NotIndexed++;
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                consecutiveErrors++;
                result.FirstError ??= ApiErrorMessage.Sanitize(ex);
                if (result.UrlsChecked == 0 && consecutiveErrors >= 3)
                {
                    progress?.Report(result);
                    break;
                }
            }

            progress?.Report(result);
        }

        return result;
    }

    public async Task<ConnectionTestResult> TestAsync(string clientId)
    {
        clientId = clientId.Trim();
        var clientSecret = _vault.GetSearchConsoleClientSecret(clientId);
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            return new ConnectionTestResult { Ok = false, Message = "No Google client secret saved" };
        }

        var refreshToken = _vault.GetSearchConsoleRefreshToken(clientId);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new ConnectionTestResult { Ok = false, Message = "No Google refresh token saved. Connect Google account first." };
        }

        try
        {
            var accessToken = await _oauthClient.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken).ConfigureAwait(false);
            var sites = await _client.ListSitesAsync(accessToken).ConfigureAwait(false);
            return new ConnectionTestResult
            {
                Ok = true,
                Message = sites.Count == 0
                    ? "Search Console authorization works, but Google returned no properties"
                    : $"Search Console authorization works; first property: {sites[0]}",
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
        }
    }
}
