using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;

namespace Numeris.Services.Database.Repositories;

public sealed class SearchConsoleRepository
{
    private readonly SqliteDatabase _db;

    public SearchConsoleRepository(SqliteDatabase db) => _db = db;

    public Task<List<SearchDay>> GetSearchDailyAsync(string siteUrl, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = siteUrl == "all"
                ? """
                  SELECT date AS Date,
                         SUM(clicks) AS TotalClicks,
                         SUM(impressions) AS TotalImpressions,
                         AVG(position) AS AvgPosition
                  FROM search_console
                  WHERE (kind = 'daily' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'daily'))
                    AND date >= @start AND date <= @end
                  GROUP BY date ORDER BY date
                  """
                : """
                  SELECT date AS Date,
                         SUM(clicks) AS TotalClicks,
                         SUM(impressions) AS TotalImpressions,
                         AVG(position) AS AvgPosition
                  FROM search_console
                  WHERE (kind = 'daily' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'daily' AND site_url = @siteUrl))
                    AND site_url = @siteUrl AND date >= @start AND date <= @end
                  GROUP BY date ORDER BY date
                  """;
            return new List<SearchDay>(connection.Query<SearchDay>(sql, new { siteUrl, start, end }));
        });
    }

    public Task<List<SearchQuery>> GetQueriesAsync(string siteUrl, string start, string end, string sortBy, int limit)
    {
        return _db.ReadAsync(connection =>
        {
            var orderClause = sortBy switch
            {
                "impressions" => "Impressions DESC",
                "ctr" => "Ctr DESC",
                "position" => "Position ASC",
                _ => "Clicks DESC",
            };

            var sql = siteUrl == "all"
                ? $"""
                   SELECT query AS Query,
                          SUM(clicks) AS Clicks,
                          SUM(impressions) AS Impressions,
                          CASE WHEN SUM(impressions) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr,
                          AVG(position) AS Position
                   FROM search_console
                   WHERE kind = 'query' AND date >= @start AND date <= @end
                   GROUP BY query ORDER BY {orderClause} LIMIT @limit
                   """
                : $"""
                   SELECT query AS Query,
                          SUM(clicks) AS Clicks,
                          SUM(impressions) AS Impressions,
                          CASE WHEN SUM(impressions) > 0 THEN CAST(SUM(clicks) AS REAL) / SUM(impressions) ELSE 0 END AS Ctr,
                          AVG(position) AS Position
                   FROM search_console
                   WHERE kind = 'query' AND site_url = @siteUrl AND date >= @start AND date <= @end
                   GROUP BY query ORDER BY {orderClause} LIMIT @limit
                   """;
            return new List<SearchQuery>(connection.Query<SearchQuery>(sql, new { siteUrl, start, end, limit }));
        });
    }

    public Task<List<SearchPageRow>> GetPagesAsync(string siteUrl, string start, string end, int limit)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = siteUrl == "all"
                ? """
                  SELECT page AS Page,
                         SUM(clicks) AS Clicks,
                         SUM(impressions) AS Impressions
                  FROM search_console
                  WHERE (kind = 'page' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'page'))
                    AND date >= @start AND date <= @end AND page IS NOT NULL
                  GROUP BY page ORDER BY Clicks DESC LIMIT @limit
                  """
                : """
                  SELECT page AS Page,
                         SUM(clicks) AS Clicks,
                         SUM(impressions) AS Impressions
                  FROM search_console
                  WHERE (kind = 'page' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'page' AND site_url = @siteUrl))
                    AND site_url = @siteUrl AND date >= @start AND date <= @end AND page IS NOT NULL
                  GROUP BY page ORDER BY Clicks DESC LIMIT @limit
                  """;
            return new List<SearchPageRow>(connection.Query<SearchPageRow>(sql, new { siteUrl, start, end, limit }));
        });
    }

    public Task<List<SearchDeviceDay>> GetDevicesAsync(string siteUrl, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = siteUrl == "all"
                ? """
                  SELECT date AS Date, device AS Device,
                         SUM(clicks) AS Clicks,
                         SUM(impressions) AS Impressions,
                         AVG(ctr) AS Ctr,
                         AVG(position) AS Position
                  FROM search_devices
                  WHERE date >= @start AND date <= @end
                  GROUP BY date, device ORDER BY date, device
                  """
                : """
                  SELECT date AS Date, device AS Device,
                         clicks AS Clicks,
                         impressions AS Impressions,
                         ctr AS Ctr,
                         position AS Position
                  FROM search_devices
                  WHERE site_url = @siteUrl AND date >= @start AND date <= @end
                  ORDER BY date, device
                  """;
            return new List<SearchDeviceDay>(connection.Query<SearchDeviceDay>(sql, new { siteUrl, start, end }));
        });
    }

    public Task<List<CountryData>> GetCountriesAsync(string siteUrl, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = siteUrl == "all"
                ? """
                  SELECT country AS Country, SUM(clicks) AS Value
                  FROM search_console
                  WHERE (kind = 'country' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'country'))
                    AND date >= @start AND date <= @end AND country IS NOT NULL
                  GROUP BY country ORDER BY Value DESC LIMIT 10
                  """
                : """
                  SELECT country AS Country, SUM(clicks) AS Value
                  FROM search_console
                  WHERE (kind = 'country' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'country' AND site_url = @siteUrl))
                    AND site_url = @siteUrl AND date >= @start AND date <= @end AND country IS NOT NULL
                  GROUP BY country ORDER BY Value DESC LIMIT 10
                  """;
            return new List<CountryData>(connection.Query<CountryData>(sql, new { siteUrl, start, end }));
        });
    }

    public Task<List<SearchQuery>> GetNewQueriesAsync(string siteUrl, string currentStart, string currentEnd, string prevStart, string prevEnd, int limit = 20)
    {
        return _db.ReadAsync(connection =>
        {
            const string sql = """
                WITH curr AS (
                    SELECT query,
                           SUM(clicks) AS clicks,
                           SUM(impressions) AS impressions,
                           AVG(ctr) AS ctr,
                           AVG(position) AS position
                    FROM search_console
                    WHERE site_url = @siteUrl AND kind = 'query'
                      AND date BETWEEN @currentStart AND @currentEnd
                      AND query IS NOT NULL AND query != '(not set)'
                    GROUP BY query
                ),
                prev AS (
                    SELECT DISTINCT query
                    FROM search_console
                    WHERE site_url = @siteUrl AND kind = 'query'
                      AND date BETWEEN @prevStart AND @prevEnd
                      AND query IS NOT NULL AND query != '(not set)'
                )
                SELECT curr.query AS Query, curr.clicks AS Clicks, curr.impressions AS Impressions,
                       curr.ctr AS Ctr, curr.position AS Position
                FROM curr
                LEFT JOIN prev ON prev.query = curr.query
                WHERE prev.query IS NULL
                ORDER BY curr.clicks DESC, curr.impressions DESC
                LIMIT @limit
                """;
            return new List<SearchQuery>(connection.Query<SearchQuery>(sql,
                new { siteUrl, currentStart, currentEnd, prevStart, prevEnd, limit }));
        });
    }

    public Task<List<DecliningPage>> GetDecliningPagesAsync(string siteUrl, string currentStart, string currentEnd, string prevStart, string prevEnd, int limit = 10, long minPrev = 5)
    {
        return _db.ReadAsync(connection =>
        {
            const string sql = """
                WITH curr AS (
                    SELECT page, SUM(clicks) AS clicks
                    FROM search_console
                    WHERE site_url = @siteUrl AND kind = 'page'
                      AND date BETWEEN @currentStart AND @currentEnd
                      AND page IS NOT NULL AND page != ''
                    GROUP BY page
                ),
                prev AS (
                    SELECT page, SUM(clicks) AS clicks
                    FROM search_console
                    WHERE site_url = @siteUrl AND kind = 'page'
                      AND date BETWEEN @prevStart AND @prevEnd
                      AND page IS NOT NULL AND page != ''
                    GROUP BY page
                )
                SELECT prev.page AS Page,
                       COALESCE(curr.clicks, 0) AS ClicksCurrent,
                       prev.clicks AS ClicksPrevious,
                       COALESCE(curr.clicks, 0) - prev.clicks AS Delta
                FROM prev
                LEFT JOIN curr ON curr.page = prev.page
                WHERE prev.clicks >= @minPrev
                  AND COALESCE(curr.clicks, 0) - prev.clicks < 0
                ORDER BY Delta ASC
                LIMIT @limit
                """;
            return new List<DecliningPage>(connection.Query<DecliningPage>(sql,
                new { siteUrl, currentStart, currentEnd, prevStart, prevEnd, minPrev, limit }));
        });
    }

    public Task<long> ReplaceSearchAnalyticsRowsAsync(
        string siteUrl,
        SearchQueryKind kind,
        IReadOnlyCollection<SearchConsoleApiRow> rows,
        string start,
        string end,
        string fetchedAt)
    {
        return _db.WriteAsync(connection =>
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
                "DELETE FROM search_console WHERE site_url = @siteUrl AND kind = @kind AND date BETWEEN @start AND @end",
                new { siteUrl, kind = kindStr, start, end });

            if (kind == SearchQueryKind.Device)
            {
                connection.Execute(
                    "DELETE FROM search_devices WHERE site_url = @siteUrl AND date BETWEEN @start AND @end",
                    new { siteUrl, start, end });
            }

            long records = 0;
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
                        siteUrl,
                        date = row.Date,
                        kind = kindStr,
                        query = row.Query,
                        page = row.Page,
                        clicks = row.Clicks,
                        impressions = row.Impressions,
                        ctr = row.Ctr,
                        position = row.Position,
                        country = row.Country,
                        fetchedAt,
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
                            siteUrl,
                            date = row.Date,
                            device = row.Device,
                            clicks = row.Clicks,
                            impressions = row.Impressions,
                            ctr = row.Ctr,
                            position = row.Position,
                        });
                }
            }

            return records;
        });
    }

    public Task<long> ReplacePageQueryRowsAsync(
        string siteUrl,
        string periodStart,
        string periodEnd,
        IReadOnlyCollection<SearchConsoleApiRow> rows)
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute(
                "DELETE FROM search_page_queries WHERE site_url = @siteUrl AND period_start = @periodStart AND period_end = @periodEnd",
                new { siteUrl, periodStart, periodEnd });

            long records = 0;
            foreach (var row in rows.Where(r => !string.IsNullOrEmpty(r.Page)))
            {
                connection.Execute(
                    """
                    INSERT OR REPLACE INTO search_page_queries
                    (site_url, period_start, period_end, page, query, clicks, impressions, ctr, position)
                    VALUES (@siteUrl, @periodStart, @periodEnd, @page, @query, @clicks, @impressions, @ctr, @position)
                    """,
                    new
                    {
                        siteUrl,
                        periodStart,
                        periodEnd,
                        page = row.Page!,
                        query = row.Query,
                        clicks = row.Clicks,
                        impressions = row.Impressions,
                        ctr = row.Ctr,
                        position = row.Position,
                    });
                records++;
            }

            return records;
        });
    }
}
