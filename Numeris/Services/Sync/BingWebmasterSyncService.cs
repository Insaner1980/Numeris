using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class BingWebmasterSyncService
{
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
            return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
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
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetCrawlIssues", siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += await StoreSiteMethodAsync(apiKey, "GetCrawlStats", siteUrl, fetchedAt).ConfigureAwait(false);

            result.RankRows += await SyncRankAndTrafficAsync(apiKey, siteUrl, fetchedAt).ConfigureAwait(false);
            var queries = await SyncQueryStatsAsync(apiKey, siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += queries.rawItems;
            result.QueryRows += queries.rows;

            var pages = await SyncPageStatsAsync(apiKey, siteUrl, fetchedAt).ConfigureAwait(false);
            result.RawItems += pages.rawItems;
            result.PageRows += pages.rows;
        }

        await _bingRepo.ApplyRawRetentionAsync(DateTime.Now
            .AddDays(-RawJsonStoragePolicy.BingRawRetentionDays)
            .ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
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
        catch (ApiRequestException ex)
        {
            await _bingRepo.UpsertRawItemAsync(new BingRawItem
            {
                Method = method,
                SiteUrl = siteUrl,
                ItemKey = "error",
                RawJson = RawJsonStoragePolicy.TrimRawJson(JsonSerializer.Serialize(new { error = ApiErrorMessage.Sanitize(ex), provider = ex.Provider, operation = ex.Operation, statusCode = (int)ex.StatusCode })),
                FetchedAt = fetchedAt,
            }).ConfigureAwait(false);
            return 1;
        }
        catch (Exception ex)
        {
            await _bingRepo.UpsertRawItemAsync(new BingRawItem
            {
                Method = method,
                SiteUrl = siteUrl,
                ItemKey = "error",
                RawJson = RawJsonStoragePolicy.TrimRawJson(JsonSerializer.Serialize(new { error = ApiErrorMessage.Sanitize(ex) })),
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
                RawJson = RawJsonStoragePolicy.TrimRawJson(item.GetRawText()),
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
            var rawJson = RawJsonStoragePolicy.TrimRawJson(item.GetRawText());
            await _bingRepo.UpsertRankTrafficWithRawItemAsync(
                siteUrl,
                date,
                FirstLong(item, "Clicks", "clicks"),
                FirstLong(item, "Impressions", "impressions"),
                rawJson,
                fetchedAt,
                new BingRawItem { Method = "GetRankAndTrafficStats", SiteUrl = siteUrl, ItemKey = date, RawJson = rawJson, FetchedAt = fetchedAt }).ConfigureAwait(false);
            rows++;
        }
        return rows;
    }

    private async Task<(long rows, long rawItems)> SyncQueryStatsAsync(string apiKey, string siteUrl, string fetchedAt)
    {
        using var doc = await _client.CallAsync(apiKey, "GetQueryStats", new Dictionary<string, string?> { ["siteUrl"] = siteUrl }).ConfigureAwait(false);
        var rows = 0L;
        var raw = 0L;
        foreach (var item in EnumerateItems(BingWebmasterClient.Unwrap(doc)))
        {
            var query = FirstString(item, "Query", "query", "Keyword", "keyword") ?? ExtractKey(item);
            if (string.IsNullOrWhiteSpace(query)) continue;
            var date = FirstString(item, "Date", "date") ?? "";
            var rawJson = RawJsonStoragePolicy.TrimRawJson(item.GetRawText());
            await _bingRepo.UpsertQueryStatsWithRawItemAsync(
                siteUrl,
                query,
                date,
                FirstLong(item, "Clicks", "clicks"),
                FirstLong(item, "Impressions", "impressions"),
                FirstDouble(item, "AvgClickPosition", "avgClickPosition", "AverageClickPosition"),
                FirstDouble(item, "AvgImpressionPosition", "avgImpressionPosition", "AverageImpressionPosition"),
                rawJson,
                fetchedAt,
                new BingRawItem { Method = "GetQueryStats", SiteUrl = siteUrl, ItemKey = BuildRawItemKey(query, date), RawJson = rawJson, FetchedAt = fetchedAt }).ConfigureAwait(false);
            rows++;
            raw++;
        }
        return (rows, raw);
    }

    private async Task<(long rows, long rawItems)> SyncPageStatsAsync(string apiKey, string siteUrl, string fetchedAt)
    {
        using var doc = await _client.CallAsync(apiKey, "GetPageStats", new Dictionary<string, string?> { ["siteUrl"] = siteUrl }).ConfigureAwait(false);
        var rows = 0L;
        var raw = 0L;
        foreach (var item in EnumerateItems(BingWebmasterClient.Unwrap(doc)))
        {
            var page = FirstString(item, "Url", "url", "Page", "page") ?? ExtractKey(item);
            if (string.IsNullOrWhiteSpace(page)) continue;
            var date = FirstString(item, "Date", "date") ?? "";
            var rawJson = RawJsonStoragePolicy.TrimRawJson(item.GetRawText());
            await _bingRepo.UpsertPageStatsWithRawItemAsync(
                siteUrl,
                page,
                date,
                FirstLong(item, "Clicks", "clicks"),
                FirstLong(item, "Impressions", "impressions"),
                rawJson,
                fetchedAt,
                new BingRawItem { Method = "GetPageStats", SiteUrl = siteUrl, ItemKey = BuildRawItemKey(page, date), RawJson = rawJson, FetchedAt = fetchedAt }).ConfigureAwait(false);
            rows++;
            raw++;
        }
        return (rows, raw);
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

    private static string BuildRawItemKey(string key, string date)
        => string.IsNullOrWhiteSpace(date) ? key : $"{key}|{date}";

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
