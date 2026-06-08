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
    private readonly SqliteDatabase _db;

    public InsightMetricsRepository(SqliteDatabase db) => _db = db;

    public Task<InsightMetrics> GetInsightMetricsAsync(string domainOrAll, int days)
    {
        return _db.ReadAsync(connection =>
        {
            var range = DateWindow(days);
            var cloudflareFilter = DomainFilter(domainOrAll, "domain");
            var searchFilter = SearchConsoleFilter(domainOrAll);
            var ga4Filter = DomainFilter(domainOrAll, "domain");
            var pageSpeedFilter = PageSpeedFilter(domainOrAll);
            var bingFilter = BingFilter(domainOrAll);
            var sitemapFilter = DomainFilter(domainOrAll, "domain");

            return new InsightMetrics(
                CloudflareVisitors: SumWindow(connection, "cloudflare_traffic", "unique_visitors", "date", range, cloudflareFilter),
                Ga4Users: SumWindow(connection, "google_analytics_daily", "active_users", "date", range, ga4Filter),
                GoogleImpressions: SearchConsoleWindow(connection, "impressions", range, searchFilter),
                GoogleClicks: SearchConsoleWindow(connection, "clicks", range, searchFilter),
                GoogleMobileClicks: SearchDeviceWindow(connection, "clicks", range, searchFilter),
                GoogleMobileImpressions: SearchDeviceWindow(connection, "impressions", range, searchFilter),
                PageSpeedMobileScore: PageSpeedMobileScoreWindow(connection, range, pageSpeedFilter),
                BingImpressions: SumWindow(connection, "bing_rank_traffic", "impressions", "date", range, bingFilter),
                BingClicks: SumWindow(connection, "bing_rank_traffic", "clicks", "date", range, bingFilter),
                Ga4EngagementRate: Ga4EngagementRateWindow(connection, range, ga4Filter),
                Ga4KeyEvents: SumWindow(connection, "google_analytics_events", "key_events", "period_start", range, ga4Filter),
                CloudflareCacheHitRatio: CacheHitRatioWindow(connection, range, cloudflareFilter),
                CloudflareThreats: SumWindow(connection, "cloudflare_traffic", "threats", "date", range, cloudflareFilter),
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
        DateWindowRange range,
        Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT COALESCE(SUM({column}), 0)
                 FROM {table}
                 WHERE {dateColumn} >= @start AND {dateColumn} <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT COALESCE(SUM({column}), 0)
                 FROM {table}
                 WHERE {dateColumn} >= @prevStart AND {dateColumn} < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range));
    }

    private static MetricWindow SearchConsoleWindow(SqliteConnection connection, string column, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT COALESCE(SUM({column}), 0)
                 FROM search_console
                 WHERE (kind = 'daily' OR NOT EXISTS (
                    SELECT 1 FROM search_console WHERE kind = 'daily' {filter.WhereClause}
                 ))
                   AND date >= @start AND date <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT COALESCE(SUM({column}), 0)
                 FROM search_console
                 WHERE (kind = 'daily' OR NOT EXISTS (
                    SELECT 1 FROM search_console WHERE kind = 'daily' {filter.WhereClause}
                 ))
                   AND date >= @prevStart AND date < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range));
    }

    private static MetricWindow SearchDeviceWindow(SqliteConnection connection, string column, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT COALESCE(SUM({column}), 0)
                 FROM search_devices
                 WHERE LOWER(device) = 'mobile'
                   AND date >= @start AND date <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT COALESCE(SUM({column}), 0)
                 FROM search_devices
                 WHERE LOWER(device) = 'mobile'
                   AND date >= @prevStart AND date < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range));
    }

    private static MetricWindow PageSpeedMobileScoreWindow(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT COALESCE(AVG(performance_score), 0)
                 FROM pagespeed_runs
                 WHERE strategy = 'MOBILE'
                   AND analysis_utc >= @start AND analysis_utc < @endExclusive
                   {filter.WhereClause}) AS Current,
                (SELECT COALESCE(AVG(performance_score), 0)
                 FROM pagespeed_runs
                 WHERE strategy = 'MOBILE'
                   AND analysis_utc >= @prevStart AND analysis_utc < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range));
    }

    private static MetricWindow Ga4EngagementRateWindow(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT COALESCE(CAST(SUM(engaged_sessions) AS REAL) / NULLIF(SUM(sessions), 0), 0)
                 FROM google_analytics_daily
                 WHERE date >= @start AND date <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT COALESCE(CAST(SUM(engaged_sessions) AS REAL) / NULLIF(SUM(sessions), 0), 0)
                 FROM google_analytics_daily
                 WHERE date >= @prevStart AND date < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range));
    }

    private static MetricWindow CacheHitRatioWindow(SqliteConnection connection, DateWindowRange range, Filter filter)
    {
        var sql = $"""
            SELECT
                (SELECT COALESCE(CAST(SUM(cached_requests) AS REAL) / NULLIF(SUM(requests), 0), 0)
                 FROM cloudflare_traffic
                 WHERE date >= @start AND date <= @end
                   {filter.WhereClause}) AS Current,
                (SELECT COALESCE(CAST(SUM(cached_requests) AS REAL) / NULLIF(SUM(requests), 0), 0)
                 FROM cloudflare_traffic
                 WHERE date >= @prevStart AND date < @start
                   {filter.WhereClause}) AS Previous
            """;
        return QueryWindow(connection, sql, filter.WithRange(range));
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
            WHERE id IN ('cf', 'wa', 'sc', 'ga4', 'crux', 'pagespeed', 'bing')
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
                   COALESCE(curr.clicks, 0) AS CurrentClicks,
                   prev.clicks AS PreviousClicks
            FROM prev
            LEFT JOIN curr ON curr.page = prev.page
            WHERE prev.clicks >= 5
              AND COALESCE(curr.clicks, 0) < prev.clicks
            ORDER BY COALESCE(curr.clicks, 0) - prev.clicks ASC
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
              AND curr.clicks > 0
            """,
            filter.WithRange(range));
    }

    private static MetricWindow QueryWindow(SqliteConnection connection, string sql, DynamicParameters parameters)
    {
        var row = connection.QuerySingle<(double Current, double Previous)>(sql, parameters);
        return new MetricWindow(row.Current, row.Previous);
    }

    private static DateWindowRange DateWindow(int days)
    {
        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-days);
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
        parameters.Add("domain", SiteIdentity.NormalizeDomain(domainOrAll));
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
        return new Filter("AND site_url = @siteUrl", parameters);
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

