using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Performance;

namespace Numeris.Services.Database.Repositories;

public sealed class BingRepository
{
    private readonly SqliteDatabase _db;

    public BingRepository(SqliteDatabase db) => _db = db;

    public Task<List<string>> ListSitesAsync(bool enabledOnly = false)
        => _db.ReadAsync(connection =>
        {
            var where = enabledOnly ? "WHERE enabled = 1" : "";
            return connection.Query<string>(
                $"""
                SELECT site_url
                FROM bing_sites
                {where}
                ORDER BY site_url
                """).AsList();
        });

    public Task AddSiteAsync(string siteUrl, string source = "manual")
        => _db.WriteAsync(connection =>
        {
            var url = PerformanceUrl.NormalizePageUrl(siteUrl);
            connection.Execute(
                """
                INSERT INTO bing_sites (site_url, source, enabled, discovered_at)
                VALUES (@url, @source, 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'))
                ON CONFLICT(site_url) DO UPDATE SET enabled = 1, source = excluded.source
                """,
                new { url, source });
        });

    public Task DeleteSiteAsync(string siteUrl)
        => _db.WriteAsync(connection =>
        {
            connection.Execute("DELETE FROM bing_sites WHERE site_url = @siteUrl", new { siteUrl });
        });

    public Task UpsertRawItemAsync(BingRawItem item)
        => _db.WriteAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO bing_raw_items (method, site_url, item_key, raw_json, fetched_at)
                VALUES (@Method, @SiteUrl, @ItemKey, @RawJson, @FetchedAt)
                ON CONFLICT(method, site_url, item_key) DO UPDATE SET
                    raw_json = excluded.raw_json,
                    fetched_at = excluded.fetched_at
                """,
                item);
        });

    public Task UpsertRankTrafficAsync(string siteUrl, string date, long? clicks, long? impressions, string rawJson, string fetchedAt)
        => _db.WriteAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO bing_rank_traffic (site_url, date, clicks, impressions, raw_json, fetched_at)
                VALUES (@siteUrl, @date, @clicks, @impressions, @rawJson, @fetchedAt)
                ON CONFLICT(site_url, date) DO UPDATE SET
                    clicks = excluded.clicks,
                    impressions = excluded.impressions,
                    raw_json = excluded.raw_json,
                    fetched_at = excluded.fetched_at
                """,
                new { siteUrl, date, clicks, impressions, rawJson, fetchedAt });
        });

    public Task UpsertQueryStatsAsync(string siteUrl, string query, string date, long? clicks, long? impressions, double? avgClickPosition, double? avgImpressionPosition, string rawJson, string fetchedAt)
        => _db.WriteAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO bing_query_stats
                (site_url, query, date, clicks, impressions, avg_click_position, avg_impression_position, raw_json, fetched_at)
                VALUES (@siteUrl, @query, @date, @clicks, @impressions, @avgClickPosition, @avgImpressionPosition, @rawJson, @fetchedAt)
                ON CONFLICT(site_url, query, date) DO UPDATE SET
                    clicks = excluded.clicks,
                    impressions = excluded.impressions,
                    avg_click_position = excluded.avg_click_position,
                    avg_impression_position = excluded.avg_impression_position,
                    raw_json = excluded.raw_json,
                    fetched_at = excluded.fetched_at
                """,
                new { siteUrl, query, date, clicks, impressions, avgClickPosition, avgImpressionPosition, rawJson, fetchedAt });
        });

    public Task UpsertPageStatsAsync(string siteUrl, string pageUrl, long? clicks, long? impressions, string rawJson, string fetchedAt)
        => _db.WriteAsync(connection =>
        {
            connection.Execute(
                """
                INSERT INTO bing_page_stats (site_url, page_url, clicks, impressions, raw_json, fetched_at)
                VALUES (@siteUrl, @pageUrl, @clicks, @impressions, @rawJson, @fetchedAt)
                ON CONFLICT(site_url, page_url) DO UPDATE SET
                    clicks = excluded.clicks,
                    impressions = excluded.impressions,
                    raw_json = excluded.raw_json,
                    fetched_at = excluded.fetched_at
                """,
                new { siteUrl, pageUrl, clicks, impressions, rawJson, fetchedAt });
        });
}
