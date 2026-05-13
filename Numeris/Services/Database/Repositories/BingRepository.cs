using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Database;
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

    public Task<List<BingTrafficDay>> GetTrafficDailyAsync(string siteUrl, string start, string end)
        => _db.ReadAsync(connection =>
        {
            var sql = IsAll(siteUrl)
                ? """
                  SELECT date AS Date,
                         COALESCE(SUM(clicks), 0) AS Clicks,
                         COALESCE(SUM(impressions), 0) AS Impressions,
                         CASE WHEN COALESCE(SUM(impressions), 0) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr
                  FROM bing_rank_traffic
                  WHERE date >= @start AND date <= @end
                  GROUP BY date
                  ORDER BY date
                  """
                : """
                  SELECT date AS Date,
                         COALESCE(SUM(clicks), 0) AS Clicks,
                         COALESCE(SUM(impressions), 0) AS Impressions,
                         CASE WHEN COALESCE(SUM(impressions), 0) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr
                  FROM bing_rank_traffic
                  WHERE site_url = @normalizedSiteUrl AND date >= @start AND date <= @end
                  GROUP BY date
                  ORDER BY date
                  """;
            return connection.Query<BingTrafficDay>(sql, new { normalizedSiteUrl = NormalizeSiteUrl(siteUrl), start, end }).AsList();
        });

    public Task<List<BingQueryRow>> GetQueriesAsync(string siteUrl, string start, string end, string sortBy, int limit)
        => _db.ReadAsync(connection =>
        {
            var orderClause = sortBy switch
            {
                "impressions" => "Impressions DESC",
                "ctr" => "Ctr DESC",
                "position" => "AvgImpressionPosition ASC",
                _ => "Clicks DESC",
            };
            var sql = IsAll(siteUrl)
                ? $"""
                   SELECT query AS Query,
                          COALESCE(SUM(clicks), 0) AS Clicks,
                          COALESCE(SUM(impressions), 0) AS Impressions,
                          CASE WHEN COALESCE(SUM(impressions), 0) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr,
                          COALESCE(AVG(avg_click_position), 0) AS AvgClickPosition,
                          COALESCE(AVG(avg_impression_position), 0) AS AvgImpressionPosition
                   FROM bing_query_stats
                   WHERE date >= @start AND date <= @end
                   GROUP BY query
                   ORDER BY {orderClause}
                   LIMIT @limit
                   """
                : $"""
                   SELECT query AS Query,
                          COALESCE(SUM(clicks), 0) AS Clicks,
                          COALESCE(SUM(impressions), 0) AS Impressions,
                          CASE WHEN COALESCE(SUM(impressions), 0) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr,
                          COALESCE(AVG(avg_click_position), 0) AS AvgClickPosition,
                          COALESCE(AVG(avg_impression_position), 0) AS AvgImpressionPosition
                   FROM bing_query_stats
                   WHERE site_url = @normalizedSiteUrl AND date >= @start AND date <= @end
                   GROUP BY query
                   ORDER BY {orderClause}
                   LIMIT @limit
                   """;
            return connection.Query<BingQueryRow>(sql, new { normalizedSiteUrl = NormalizeSiteUrl(siteUrl), start, end, limit }).AsList();
        });

    public Task<List<BingPageRow>> GetPagesAsync(string siteUrl, string start, string end, int limit)
        => _db.ReadAsync(connection =>
        {
            var sql = IsAll(siteUrl)
                ? """
                  SELECT page_url AS PageUrl,
                         COALESCE(SUM(clicks), 0) AS Clicks,
                         COALESCE(SUM(impressions), 0) AS Impressions,
                         CASE WHEN COALESCE(SUM(impressions), 0) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr
                  FROM bing_page_stats
                  WHERE date >= @start AND date <= @end
                  GROUP BY page_url
                  ORDER BY Clicks DESC
                  LIMIT @limit
                  """
                : """
                  SELECT page_url AS PageUrl,
                         COALESCE(SUM(clicks), 0) AS Clicks,
                         COALESCE(SUM(impressions), 0) AS Impressions,
                         CASE WHEN COALESCE(SUM(impressions), 0) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr
                  FROM bing_page_stats
                  WHERE site_url = @normalizedSiteUrl AND date >= @start AND date <= @end
                  GROUP BY page_url
                  ORDER BY Clicks DESC
                  LIMIT @limit
                  """;
            return connection.Query<BingPageRow>(sql, new { normalizedSiteUrl = NormalizeSiteUrl(siteUrl), start, end, limit }).AsList();
        });

    public Task<List<BingRawMethodSummary>> GetRawMethodSummaryAsync(string siteUrl)
        => _db.ReadAsync(connection =>
        {
            var sql = IsAll(siteUrl)
                ? """
                  SELECT method AS Method, COUNT(*) AS ItemCount, COALESCE(MAX(fetched_at), '') AS LastFetchedAt
                  FROM bing_raw_items
                  GROUP BY method
                  ORDER BY method
                  """
                : """
                  SELECT method AS Method, COUNT(*) AS ItemCount, COALESCE(MAX(fetched_at), '') AS LastFetchedAt
                  FROM bing_raw_items
                  WHERE site_url = @normalizedSiteUrl
                  GROUP BY method
                  ORDER BY method
                  """;
            return connection.Query<BingRawMethodSummary>(sql, new { normalizedSiteUrl = NormalizeSiteUrl(siteUrl) }).AsList();
        });

    public Task<List<BingRawItem>> GetCrawlIssueItemsAsync(string siteUrl, int limit)
        => _db.ReadAsync(connection =>
        {
            var sql = IsAll(siteUrl)
                ? """
                  SELECT method AS Method, site_url AS SiteUrl, item_key AS ItemKey, raw_json AS RawJson, fetched_at AS FetchedAt
                  FROM bing_raw_items
                  WHERE method = 'GetCrawlIssues'
                  ORDER BY fetched_at DESC, item_key
                  LIMIT @limit
                  """
                : """
                  SELECT method AS Method, site_url AS SiteUrl, item_key AS ItemKey, raw_json AS RawJson, fetched_at AS FetchedAt
                  FROM bing_raw_items
                  WHERE method = 'GetCrawlIssues' AND site_url = @normalizedSiteUrl
                  ORDER BY fetched_at DESC, item_key
                  LIMIT @limit
                  """;
            return connection.Query<BingRawItem>(sql, new { normalizedSiteUrl = NormalizeSiteUrl(siteUrl), limit }).AsList();
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
            item.RawJson = RawJsonStoragePolicy.TrimRawJson(item.RawJson);
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

    public Task UpsertRankTrafficWithRawItemAsync(
        string siteUrl,
        string date,
        long? clicks,
        long? impressions,
        string rawJson,
        string fetchedAt,
        BingRawItem rawItem)
        => _db.WriteTransactionAsync((connection, transaction) =>
        {
            rawJson = RawJsonStoragePolicy.TrimRawJson(rawJson);
            rawItem.RawJson = RawJsonStoragePolicy.TrimRawJson(rawItem.RawJson);
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
                new { siteUrl, date, clicks, impressions, rawJson, fetchedAt },
                transaction);
            UpsertRawItem(connection, transaction, rawItem);
        });

    public Task UpsertQueryStatsWithRawItemAsync(
        string siteUrl,
        string query,
        string date,
        long? clicks,
        long? impressions,
        double? avgClickPosition,
        double? avgImpressionPosition,
        string rawJson,
        string fetchedAt,
        BingRawItem rawItem)
        => _db.WriteTransactionAsync((connection, transaction) =>
        {
            rawJson = RawJsonStoragePolicy.TrimRawJson(rawJson);
            rawItem.RawJson = RawJsonStoragePolicy.TrimRawJson(rawItem.RawJson);
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
                new { siteUrl, query, date, clicks, impressions, avgClickPosition, avgImpressionPosition, rawJson, fetchedAt },
                transaction);
            UpsertRawItem(connection, transaction, rawItem);
        });

    public Task UpsertPageStatsWithRawItemAsync(
        string siteUrl,
        string pageUrl,
        string date,
        long? clicks,
        long? impressions,
        string rawJson,
        string fetchedAt,
        BingRawItem rawItem)
        => _db.WriteTransactionAsync((connection, transaction) =>
        {
            rawJson = RawJsonStoragePolicy.TrimRawJson(rawJson);
            rawItem.RawJson = RawJsonStoragePolicy.TrimRawJson(rawItem.RawJson);
            connection.Execute(
                """
                INSERT INTO bing_page_stats (site_url, page_url, date, clicks, impressions, raw_json, fetched_at)
                VALUES (@siteUrl, @pageUrl, @date, @clicks, @impressions, @rawJson, @fetchedAt)
                ON CONFLICT(site_url, page_url, date) DO UPDATE SET
                    clicks = excluded.clicks,
                    impressions = excluded.impressions,
                    raw_json = excluded.raw_json,
                    fetched_at = excluded.fetched_at
                """,
                new { siteUrl, pageUrl, date, clicks, impressions, rawJson, fetchedAt },
                transaction);
            UpsertRawItem(connection, transaction, rawItem);
        });

    public Task ApplyRawRetentionAsync(string fetchedBefore)
        => _db.WriteAsync(connection =>
        {
            connection.Execute(
                "DELETE FROM bing_raw_items WHERE fetched_at < @fetchedBefore",
                new { fetchedBefore });
        });

    private static void UpsertRawItem(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction, BingRawItem item)
    {
        connection.Execute(
            """
            INSERT INTO bing_raw_items (method, site_url, item_key, raw_json, fetched_at)
            VALUES (@Method, @SiteUrl, @ItemKey, @RawJson, @FetchedAt)
            ON CONFLICT(method, site_url, item_key) DO UPDATE SET
                raw_json = excluded.raw_json,
                fetched_at = excluded.fetched_at
            """,
            item,
            transaction);
    }

    private static bool IsAll(string siteUrl)
        => string.Equals(siteUrl, "all", System.StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSiteUrl(string siteUrl)
        => IsAll(siteUrl) ? "all" : SiteIdentity.NormalizeHomePageUrl(siteUrl);
}
