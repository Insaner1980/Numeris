using System;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;
using Numeris.Models;

namespace Numeris.Services.Database.Repositories;

public sealed class InsightMetricsRepository
{
    private const string DomainColumn = "domain";
    private const string SiteUrlColumn = "site_url";

    private readonly SqliteDatabase _db;

    public InsightMetricsRepository(SqliteDatabase db) => _db = db;

    public Task<InsightMetrics> GetInsightMetricsAsync(string domainOrAll, int days)
    {
        return _db.ReadAsync(connection =>
        {
            var range = DateWindow(days);
            var cloudflareFilter = DomainFilter(domainOrAll, DomainColumn);
            var searchFilter = SearchConsoleFilter(domainOrAll);
            var pageSpeedFilter = PageSpeedFilter(domainOrAll);
            var bingFilter = BingFilter(domainOrAll);
            var sitemapFilter = DomainFilter(domainOrAll, DomainColumn);

            return new InsightMetrics(
                CloudflareVisitors: SumWindow(connection, "cloudflare_traffic", "unique_visitors", "date", DomainColumn, range, cloudflareFilter),
                GoogleImpressions: SearchConsoleWindow(connection, "impressions", range, searchFilter),
                GoogleClicks: SearchConsoleWindow(connection, "clicks", range, searchFilter),
                GoogleMobileClicks: SearchDeviceWindow(connection, "clicks", range, searchFilter),
                GoogleMobileImpressions: SearchDeviceWindow(connection, "impressions", range, searchFilter),
                PageSpeedMobileScore: PageSpeedMobileScoreWindow(connection, range, pageSpeedFilter),
                BingImpressions: SumWindow(connection, "bing_rank_traffic", "impressions", "date", SiteUrlColumn, range, bingFilter),
                BingClicks: SumWindow(connection, "bing_rank_traffic", "clicks", "date", SiteUrlColumn, range, bingFilter),
                CloudflareCacheHitRatio: CacheHitRatioWindow(connection, range, cloudflareFilter),
                CloudflareThreats: SumWindow(connection, "cloudflare_traffic", "threats", "date", DomainColumn, range, cloudflareFilter),
                HttpStatus: StatusCodeSummary(connection, range, cloudflareFilter),
                Indexing: IndexingSummary(connection, sitemapFilter),
                Freshness: SourceFreshness(connection),
                WorstDecliningPage: IsAll(domainOrAll) ? null : WorstDecliningPage(connection, range, searchFilter),
                NewSearchQueryCount: IsAll(domainOrAll) ? 0 : NewSearchQueryCount(connection, range, searchFilter),
                IsSingleDomain: !IsAll(domainOrAll));
        });
    }

    private static MetricWindow SumWindow(
        SqliteConnection connection,
        string table,
        string column,
        string dateColumn,
        string identityColumn,
        DateWindowRange range,
        Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT CASE WHEN COUNT(*) = COUNT({column}) THEN SUM({column}) END
                 FROM {table}
                 WHERE {dateColumn} >= @start AND {dateColumn} <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT CASE WHEN COUNT(*) = COUNT({column}) THEN SUM({column}) END
                 FROM {table}
                 WHERE {dateColumn} >= @prevStart AND {dateColumn} < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range)) with
        {
            HasComparison = HasPairedHistory(connection, table, identityColumn, dateColumn, $"{column} IS NOT NULL", range, filter),
        };
    }

    private static MetricWindow SearchConsoleWindow(SqliteConnection connection, string column, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT SUM({column})
                 FROM search_console
                 WHERE (kind = 'daily' OR NOT EXISTS (
                    SELECT 1 FROM search_console WHERE kind = 'daily' {filter.WhereClause}
                 ))
                   AND date >= @start AND date <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT SUM({column})
                 FROM search_console
                 WHERE (kind = 'daily' OR NOT EXISTS (
                    SELECT 1 FROM search_console WHERE kind = 'daily' {filter.WhereClause}
                 ))
                   AND date >= @prevStart AND date < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range)) with
        {
            HasComparison = HasPairedHistory(connection, "search_console", SiteUrlColumn, "date",
                "(kind = 'daily' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'daily' " + filter.WhereClause + "))", range, filter),
        };
    }

    private static MetricWindow SearchDeviceWindow(SqliteConnection connection, string column, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT SUM({column})
                 FROM search_devices
                 WHERE LOWER(device) = 'mobile'
                   AND date >= @start AND date <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT SUM({column})
                 FROM search_devices
                 WHERE LOWER(device) = 'mobile'
                   AND date >= @prevStart AND date < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range)) with
        {
            HasComparison = HasPairedHistory(connection, "search_devices", SiteUrlColumn, "date", "LOWER(device) = 'mobile'", range, filter),
        };
    }

    private static MetricWindow PageSpeedMobileScoreWindow(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT AVG(performance_score)
                 FROM pagespeed_runs
                 WHERE strategy = 'MOBILE'
                   AND analysis_utc >= @start AND analysis_utc < @endExclusive
                   {filter.WhereClause}) AS Current,
                (SELECT AVG(performance_score)
                 FROM pagespeed_runs
                 WHERE strategy = 'MOBILE'
                   AND analysis_utc >= @prevStart AND analysis_utc < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range)) with
        {
            HasComparison = HasPairedHistory(connection, "pagespeed_runs", "url", "analysis_utc",
                "strategy = 'MOBILE' AND performance_score IS NOT NULL", range, filter),
        };
    }

    private static MetricWindow CacheHitRatioWindow(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT CAST(SUM(cached_requests) AS REAL) / NULLIF(SUM(requests), 0)
                 FROM cloudflare_traffic
                 WHERE date >= @start AND date <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT CAST(SUM(cached_requests) AS REAL) / NULLIF(SUM(requests), 0)
                 FROM cloudflare_traffic
                 WHERE date >= @prevStart AND date < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range)) with
        {
            HasComparison = HasPairedHistory(connection, "cloudflare_traffic", DomainColumn, "date", "requests > 0", range, filter),
        };
    }

    private static StatusCodeSummary StatusCodeSummary(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        var parameters = filter.WithRange(range);
        var totalRequests = connection.ExecuteScalar<long>(
            $"""
            SELECT COALESCE(SUM(requests), 0)
            FROM cloudflare_traffic
            WHERE date >= @start AND date <= @end
              {filter.WhereClause}
            """,
            parameters);
        var errors = connection.QuerySingle<(long ClientErrors, long ServerErrors)>(
            $"""
            SELECT
                COALESCE(SUM(CASE WHEN status_code >= 400 AND status_code < 500 THEN requests ELSE 0 END), 0) AS ClientErrors,
                COALESCE(SUM(CASE WHEN status_code >= 500 AND status_code < 600 THEN requests ELSE 0 END), 0) AS ServerErrors
            FROM cloudflare_status_codes
            WHERE date >= @start AND date <= @end
              {filter.WhereClause}
            """,
            parameters);
        return new StatusCodeSummary(totalRequests, errors.ClientErrors, errors.ServerErrors);
    }

    private static IndexingSummary IndexingSummary(SqliteConnection connection, Filter filter)
    {
        var summary = connection.QuerySingle<(int Active, int Inspected, int Indexed)>(
            $"""
            SELECT
                COALESCE(SUM(CASE WHEN removed_at IS NULL THEN 1 ELSE 0 END), 0) AS Active,
                COALESCE(SUM(CASE WHEN removed_at IS NULL AND (
                    verdict IS NOT NULL OR coverage_state IS NOT NULL OR indexing_state IS NOT NULL
                ) THEN 1 ELSE 0 END), 0) AS Inspected,
                COALESCE(SUM(CASE WHEN removed_at IS NULL AND (
                    UPPER(COALESCE(verdict, '')) = 'PASS'
                    OR coverage_state LIKE 'Indexed%'
                ) THEN 1 ELSE 0 END), 0) AS Indexed
            FROM sitemap_urls
            WHERE 1 = 1
              {filter.WhereClause}
            """,
            filter.Parameters);
        return new IndexingSummary(summary.Active, summary.Inspected, summary.Indexed);
    }

    private static SourceFreshnessSummary SourceFreshness(SqliteConnection connection)
    {
        var cutoff = DateTime.Now.AddHours(-48).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var summary = connection.QuerySingle<(int Connected, int Missing, int Stale)>(
            """
            SELECT
                COUNT(*) AS Connected,
                COALESCE(SUM(CASE WHEN last_sync IS NULL OR last_sync = '' THEN 1 ELSE 0 END), 0) AS Missing,
                COALESCE(SUM(CASE WHEN last_sync IS NOT NULL AND last_sync != '' AND last_sync < @cutoff THEN 1 ELSE 0 END), 0) AS Stale
            FROM connections
            WHERE (id IN ('cf', 'wa', 'sc', 'crux', 'pagespeed', 'bing') OR source = 'cloudflare')
              AND status != 'disconnected'
            """,
            new { cutoff });
        return new SourceFreshnessSummary(summary.Connected, summary.Missing, summary.Stale);
    }

    private static SearchPageTrend? WorstDecliningPage(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        var row = connection.QuerySingleOrDefault<(string Page, long CurrentClicks, long PreviousClicks)>(
            $"""
            WITH curr AS (
                SELECT page, SUM(clicks) AS clicks
                FROM search_console
                WHERE kind = 'page'
                  AND date >= @start AND date <= @end
                  AND page IS NOT NULL AND page != ''
                  {filter.WhereClause}
                GROUP BY page
            ),
            prev AS (
                SELECT page, SUM(clicks) AS clicks
                FROM search_console
                WHERE kind = 'page'
                  AND date >= @prevStart AND date < @start
                  AND page IS NOT NULL AND page != ''
                  {filter.WhereClause}
                GROUP BY page
            )
            SELECT prev.page AS Page,
                   curr.clicks AS CurrentClicks,
                   prev.clicks AS PreviousClicks
            FROM prev
            JOIN curr ON curr.page = prev.page
            WHERE prev.clicks >= 5
              AND curr.clicks < prev.clicks
            ORDER BY curr.clicks - prev.clicks ASC
            LIMIT 1
            """,
            filter.WithRange(range));
        return string.IsNullOrWhiteSpace(row.Page)
            ? null
            : new SearchPageTrend(row.Page, row.CurrentClicks, row.PreviousClicks);
    }

    private static int NewSearchQueryCount(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        return connection.ExecuteScalar<int>(
            $"""
            WITH curr AS (
                SELECT query, SUM(clicks) AS clicks
                FROM search_console
                WHERE kind = 'query'
                  AND date >= @start AND date <= @end
                  AND query IS NOT NULL AND query != '' AND query != '(not set)'
                  {filter.WhereClause}
                GROUP BY query
            ),
            prev AS (
                SELECT DISTINCT query
                FROM search_console
                WHERE kind = 'query'
                  AND date >= @prevStart AND date < @start
                  AND query IS NOT NULL AND query != '' AND query != '(not set)'
                  {filter.WhereClause}
            )
            SELECT COUNT(*)
            FROM curr
            LEFT JOIN prev ON prev.query = curr.query
            WHERE prev.query IS NULL
              AND EXISTS (SELECT 1 FROM prev)
              AND curr.clicks > 0
            """,
            filter.WithRange(range));
    }

    private static MetricWindow QueryWindow(SqliteConnection connection, string sql, DynamicParameters parameters)
    {
        var row = connection.QuerySingle<(double? Current, double? Previous)>(sql, parameters);
        return new MetricWindow(row.Current ?? 0, row.Previous ?? 0, row.Current.HasValue, row.Previous.HasValue);
    }

    private static bool HasPairedHistory(SqliteConnection connection, string table, string identityColumn,
        string dateColumn, string predicate, DateWindowRange range, Filter filter)
    {
        return connection.ExecuteScalar<bool>($"""
            SELECT COUNT(*) = 0
            FROM (
                SELECT {identityColumn}
                FROM {table}
                WHERE {dateColumn} >= @prevStart AND {dateColumn} < @endExclusive
                  AND {predicate}
                  {filter.WhereClause}
                GROUP BY {identityColumn}
                HAVING MAX(CASE WHEN {dateColumn} >= @start THEN 1 ELSE 0 END) = 0
                    OR MAX(CASE WHEN {dateColumn} < @start THEN 1 ELSE 0 END) = 0
            )
            """, filter.WithRange(range));
    }

    private static DateWindowRange DateWindow(int days)
    {
        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var previousStart = startDate.AddDays(-days);
        var endExclusive = endDate.AddDays(1);
        string s(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new DateWindowRange(s(startDate), s(endDate), s(endExclusive), s(previousStart));
    }

    private static Filter DomainFilter(string domainOrAll, string column)
    {
        if (IsAll(domainOrAll))
        {
            return Filter.Empty;
        }

        var parameters = new DynamicParameters();
        parameters.Add(DomainColumn, SiteIdentity.NormalizeDomain(domainOrAll));
        return new Filter($"AND {column} = @domain", parameters);
    }

    private static Filter SearchConsoleFilter(string domainOrAll)
    {
        if (IsAll(domainOrAll))
        {
            return Filter.Empty;
        }

        var parameters = new DynamicParameters();
        parameters.Add("siteUrl", SiteIdentity.NormalizeDomain(domainOrAll));
        return new Filter("AND site_url = @siteUrl", parameters);
    }

    private static Filter BingFilter(string domainOrAll)
    {
        if (IsAll(domainOrAll))
        {
            return Filter.Empty;
        }

        var parameters = new DynamicParameters();
        parameters.Add("siteUrl", SiteIdentity.NormalizeHomePageUrl(domainOrAll));
        return new Filter("AND site_url LIKE @siteUrl || '%'", parameters);
    }

    private static Filter PageSpeedFilter(string domainOrAll)
    {
        if (IsAll(domainOrAll))
        {
            return Filter.Empty;
        }

        var identity = SiteIdentity.FromDomainOrUrl(domainOrAll);
        var parameters = new DynamicParameters();
        parameters.Add("homePageUrl", identity.HomePageUrl);
        parameters.Add("urlPrefix", identity.OriginUrl + "/%");
        return new Filter("AND (url = @homePageUrl OR url LIKE @urlPrefix)", parameters);
    }

    private static bool IsAll(string domainOrAll)
        => string.Equals(domainOrAll, "all", StringComparison.OrdinalIgnoreCase);

    private sealed record DateWindowRange(string Start, string End, string EndExclusive, string PreviousStart);

    private sealed record Filter(string WhereClause, DynamicParameters Parameters)
    {
        public static Filter Empty { get; } = new("", new DynamicParameters());

        public DynamicParameters WithRange(DateWindowRange range)
        {
            var parameters = new DynamicParameters();
            foreach (var name in Parameters.ParameterNames)
            {
                parameters.Add(name, Parameters.Get<object?>(name));
            }
            parameters.Add("start", range.Start);
            parameters.Add("end", range.End);
            parameters.Add("endExclusive", range.EndExclusive);
            parameters.Add("prevStart", range.PreviousStart);
            return parameters;
        }
    }
}

