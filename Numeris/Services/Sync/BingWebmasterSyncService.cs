using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class BingWebmasterSyncService
{
    private const int DetailLimit = 25;

    private readonly BingWebmasterClient _client;
    private readonly BingRepository _bingRepo;
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;

    public BingWebmasterSyncService(
        BingWebmasterClient client,
        BingRepository bingRepo,
        ConnectionsRepository connectionsRepo,
        CredentialVault vault)
    {
        _client = client;
        _bingRepo = bingRepo;
        _connectionsRepo = connectionsRepo;
        _vault = vault;
    }

    public Task<List<string>> ListSitesAsync()
        => _bingRepo.ListSitesAsync();

    public Task AddSiteAsync(string siteUrl)
        => _bingRepo.AddSiteAsync(siteUrl);

    public Task DeleteSiteAsync(string siteUrl)
        => _bingRepo.DeleteSiteAsync(siteUrl);

    public async Task<ConnectionTestResult> TestAsync()
    {
        var apiKey = _vault.GetBingApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ConnectionTestResult { Ok = false, Message = "No Bing Webmaster API key saved" };
        }

        try
        {
            using var doc = await _client.CallAsync(apiKey, "GetUserSites", new Dictionary<string, string?>()).ConfigureAwait(false);
            var count = EnumerateItems(BingWebmasterClient.Unwrap(doc)).Count();
            return new ConnectionTestResult { Ok = true, Message = $"Bing API works; {count} user site item(s) returned" };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ex.Message };
        }
    }

    public async Task<BingSyncResult> SyncAsync()
    {
        var apiKey = _vault.GetBingApiKey() ?? throw new InvalidOperationException("No Bing Webmaster API key saved");
        var fetchedAt = _connectionsRepo.FormatNow();
        var result = new BingSyncResult();

        result.RawItems += await StoreMethodAsync(apiKey, "GetUserSites", "", new Dictionary<string, string?>(), fetchedAt).ConfigureAwait(false);

        var sites = await _bingRepo.ListSitesAsync(enabledOnly: true).ConfigureAwait(false);
        foreach (var siteUrl in sites)
        {
            result.SitesSynced++;
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetBlockedUrls", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetConnectedPages", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetContentSubmissionQuota", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetCountryRegionSettings", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetCrawlIssues", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetCrawlSettings", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetCrawlStats", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetFeeds", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetFetchedUrls", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetQueryParameters", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetSiteMoves", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreMethodAsync(apiKey, "GetSiteRoles", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["includeChildren"] = "true" }, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetUrlSubmissionQuota", siteUrl, fetchedAt).ConfigureAwait(false);

            result.RankRows += await SyncRankAndTrafficAsync(apiKey, siteUrl, fetchedAt).ConfigureAwait(false);
            var queries = await SyncQueryStatsAsync(apiKey, siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += queries.rawItems;
            result.QueryRows += queries.rows;

            var pages = await SyncPageStatsAsync(apiKey, siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += pages.rawItems;
            result.PageRows += pages.rows;

            foreach (var query in queries.keys.Take(DetailLimit))
            {
                result.RawItems += await StoreMethodAsync(apiKey, "GetQueryTrafficStats", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["query"] = query }, fetchedAt).ConfigureAwait(false);
                result.RawItems += await StoreMethodAsync(apiKey, "GetQueryPageStats", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["query"] = query }, fetchedAt).ConfigureAwait(false);
            }

            foreach (var page in pages.keys.Take(DetailLimit))
            {
                result.RawItems += await StoreMethodAsync(apiKey, "GetPageQueryStats", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["page"] = page }, fetchedAt).ConfigureAwait(false);
                result.RawItems += await StoreMethodAsync(apiKey, "GetUrlInfo", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["url"] = page }, fetchedAt).ConfigureAwait(false);
                result.RawItems += await StoreMethodAsync(apiKey, "GetUrlTrafficInfo", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["url"] = page }, fetchedAt).ConfigureAwait(false);
                result.RawItems += await StoreMethodAsync(apiKey, "GetUrlLinks", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["url"] = page, ["count"] = "100" }, fetchedAt).ConfigureAwait(false);
                result.RawItems += await StoreMethodAsync(apiKey, "GetChildrenUrlInfo", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["url"] = page, ["count"] = "100" }, fetchedAt).ConfigureAwait(false);
                result.RawItems += await StoreMethodAsync(apiKey, "GetChildrenUrlTrafficInfo", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["url"] = page, ["count"] = "100" }, fetchedAt).ConfigureAwait(false);
                result.RawItems += await StoreMethodAsync(apiKey, "GetFetchedUrlDetails", siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["url"] = page }, fetchedAt).ConfigureAwait(false);
            }

            foreach (var pair in queries.keys.Take(DetailLimit).Zip(pages.keys.Take(DetailLimit)))
            {
                result.RawItems += await StoreMethodAsync(
                    apiKey,
                    "GetQueryPageDetailStats",
                    siteUrl,
                    new Dictionary<string, string?> { ["siteUrl"] = siteUrl, ["query"] = pair.First, ["page"] = pair.Second },
                    fetchedAt).ConfigureAwait(false);
            }
        }

        await _connectionsRepo.UpdateBingLastSyncAsync(fetchedAt).ConfigureAwait(false);
        return result;
    }

    private Task<long> StoreSiteMethodAsync(string apiKey, string method, string siteUrl, string fetchedAt)
        => StoreMethodAsync(apiKey, method, siteUrl, new Dictionary<string, string?> { ["siteUrl"] = siteUrl }, fetchedAt);

    private async Task<long> StoreMethodAsync(string apiKey, string method, string siteUrl, IReadOnlyDictionary<string, string?> parameters, string fetchedAt)
    {
        try
        {
            using var doc = await _client.CallAsync(apiKey, method, parameters).ConfigureAwait(false);
            var unwrapped = BingWebmasterClient.Unwrap(doc);
            return await StoreItemsAsync(method, siteUrl, unwrapped, fetchedAt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _bingRepo.UpsertRawItemAsync(new BingRawItem
            {
                Method = method,
                SiteUrl = siteUrl,
                ItemKey = "error",
                RawJson = JsonSerializer.Serialize(new { error = ex.Message }),
                FetchedAt = fetchedAt,
            }).ConfigureAwait(false);
            return 1;
        }
    }

    private async Task<long> StoreItemsAsync(string method, string siteUrl, JsonElement value, string fetchedAt)
    {
        long rows = 0;
        var index = 0;
        foreach (var item in EnumerateItems(value))
        {
            var key = ExtractKey(item) ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _bingRepo.UpsertRawItemAsync(new BingRawItem
            {
                Method = method,
                SiteUrl = siteUrl,
                ItemKey = key,
                RawJson = item.GetRawText(),
                FetchedAt = fetchedAt,
            }).ConfigureAwait(false);
            index++;
            rows++;
        }
        return rows;
    }

    private async Task<long> SyncRankAndTrafficAsync(string apiKey, string siteUrl, string fetchedAt)
    {
        using var doc = await _client.CallAsync(apiKey, "GetRankAndTrafficStats", new Dictionary<string, string?> { ["siteUrl"] = siteUrl }).ConfigureAwait(false);
        var rows = 0L;
        foreach (var item in EnumerateItems(BingWebmasterClient.Unwrap(doc)))
        {
            var date = FirstString(item, "Date", "date", "Day", "day") ?? ExtractKey(item) ?? fetchedAt;
            await _bingRepo.UpsertRankTrafficAsync(siteUrl, date, FirstLong(item, "Clicks", "clicks"), FirstLong(item, "Impressions", "impressions"), item.GetRawText(), fetchedAt).ConfigureAwait(false);
            await _bingRepo.UpsertRawItemAsync(new BingRawItem { Method = "GetRankAndTrafficStats", SiteUrl = siteUrl, ItemKey = date, RawJson = item.GetRawText(), FetchedAt = fetchedAt }).ConfigureAwait(false);
            rows++;
        }
        return rows;
    }

    private async Task<(long rows, long rawItems, List<string> keys)> SyncQueryStatsAsync(string apiKey, string siteUrl, string fetchedAt)
    {
        using var doc = await _client.CallAsync(apiKey, "GetQueryStats", new Dictionary<string, string?> { ["siteUrl"] = siteUrl }).ConfigureAwait(false);
        var rows = 0L;
        var raw = 0L;
        var keys = new List<string>();
        foreach (var item in EnumerateItems(BingWebmasterClient.Unwrap(doc)))
        {
            var query = FirstString(item, "Query", "query", "Keyword", "keyword") ?? ExtractKey(item);
            if (string.IsNullOrWhiteSpace(query)) continue;
            keys.Add(query);
            var date = FirstString(item, "Date", "date") ?? "";
            await _bingRepo.UpsertQueryStatsAsync(
                siteUrl,
                query,
                date,
                FirstLong(item, "Clicks", "clicks"),
                FirstLong(item, "Impressions", "impressions"),
                FirstDouble(item, "AvgClickPosition", "avgClickPosition", "AverageClickPosition"),
                FirstDouble(item, "AvgImpressionPosition", "avgImpressionPosition", "AverageImpressionPosition"),
                item.GetRawText(),
                fetchedAt).ConfigureAwait(false);
            await _bingRepo.UpsertRawItemAsync(new BingRawItem { Method = "GetQueryStats", SiteUrl = siteUrl, ItemKey = query, RawJson = item.GetRawText(), FetchedAt = fetchedAt }).ConfigureAwait(false);
            rows++;
            raw++;
        }
        return (rows, raw, keys);
    }

    private async Task<(long rows, long rawItems, List<string> keys)> SyncPageStatsAsync(string apiKey, string siteUrl, string fetchedAt)
    {
        using var doc = await _client.CallAsync(apiKey, "GetPageStats", new Dictionary<string, string?> { ["siteUrl"] = siteUrl }).ConfigureAwait(false);
        var rows = 0L;
        var raw = 0L;
        var keys = new List<string>();
        foreach (var item in EnumerateItems(BingWebmasterClient.Unwrap(doc)))
        {
            var page = FirstString(item, "Url", "url", "Page", "page") ?? ExtractKey(item);
            if (string.IsNullOrWhiteSpace(page)) continue;
            keys.Add(page);
            await _bingRepo.UpsertPageStatsAsync(siteUrl, page, FirstLong(item, "Clicks", "clicks"), FirstLong(item, "Impressions", "impressions"), item.GetRawText(), fetchedAt).ConfigureAwait(false);
            await _bingRepo.UpsertRawItemAsync(new BingRawItem { Method = "GetPageStats", SiteUrl = siteUrl, ItemKey = page, RawJson = item.GetRawText(), FetchedAt = fetchedAt }).ConfigureAwait(false);
            rows++;
            raw++;
        }
        return (rows, raw, keys);
    }

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) yield return item;
            yield break;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            yield return value;
        }
    }

    private static string? ExtractKey(JsonElement item)
        => FirstString(item, "Url", "url", "Page", "page", "Query", "query", "Name", "name", "Date", "date", "SiteUrl", "siteUrl");

    private static string? FirstString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }

    private static long? FirstLong(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var number))
            {
                return number;
            }
        }
        return null;
    }

    private static double? FirstDouble(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDouble();
            }
        }
        return null;
    }
}
