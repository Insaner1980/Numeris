using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;
using Numeris.Models;

namespace Numeris.Services.Database.Repositories;

public sealed class SummaryRepository
{
    private readonly SqliteDatabase _db;

    public SummaryRepository(SqliteDatabase db) => _db = db;

    public Task<SummaryData> GetSummaryAsync(string domainOrAll, int days)
    {
        return _db.ReadAsync(connection =>
        {
            var endDate = DateOnly.FromDateTime(DateTime.Today);
            var startDate = endDate.AddDays(-(days - 1));
            var prevStart = startDate.AddDays(-days);

            string s(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var startStr = s(startDate);
            var endStr = s(endDate);
            var prevStartStr = s(prevStart);
            var summaryDomain = NormalizeSummaryDomain(domainOrAll);
            var cloudflareWhere = SummaryDomainWhereClause(domainOrAll, "domain");
            var searchConsoleWhere = SummaryDomainWhereClause(domainOrAll, "site_url");

            var visitors = connection.QuerySingle<(long Total, int Days, string? LatestDate)>(
                $"""
                SELECT COALESCE(SUM(unique_visitors), 0) AS Total,
                       COUNT(DISTINCT date) AS Days, MAX(date) AS LatestDate
                FROM cloudflare_traffic
                WHERE date >= @a AND date <= @b
                  {cloudflareWhere}
                """,
                ToParameters(("@a", startStr), ("@b", endStr), ("@domain", summaryDomain)));
            var visitorsPrev = connection.QuerySingle<(long Total, int Days)>(
                $"""
                SELECT COALESCE(SUM(unique_visitors), 0) AS Total, COUNT(DISTINCT date) AS Days
                FROM cloudflare_traffic
                WHERE date >= @a AND date < @b
                  {cloudflareWhere}
                """,
                ToParameters(("@a", prevStartStr), ("@b", startStr), ("@domain", summaryDomain)));

            var clicksSql = """
                SELECT COALESCE(SUM(clicks), 0) AS Total,
                       COUNT(DISTINCT date) AS Days, MAX(date) AS LatestDate
                FROM search_console
                WHERE (kind = 'daily' OR NOT EXISTS (
                    SELECT 1 FROM search_console
                    WHERE kind = 'daily'
                      {1}
                ))
                  AND date >= $a AND date {0} $b
                  {1}
                """;
            var clicks = connection.QuerySingle<(long Total, int Days, string? LatestDate)>(string.Format(clicksSql, "<=", searchConsoleWhere),
                ToParameters(("$a", startStr), ("$b", endStr), ("@domain", summaryDomain)));
            var clicksPrev = connection.QuerySingle<(long Total, int Days, string? LatestDate)>(string.Format(clicksSql, "<", searchConsoleWhere),
                ToParameters(("$a", prevStartStr), ("$b", startStr), ("@domain", summaryDomain)));
            var coverageParameters = ToParameters(("@start", startStr), ("@end", endStr), ("@prevStart", prevStartStr),
                ("@domain", summaryDomain), ("@days", days));
            var visitorsComplete = HasCompleteSitePeriods(connection, "cloudflare_traffic", "domain", cloudflareWhere, coverageParameters);
            var clicksComplete = HasCompleteSitePeriods(connection, "search_console", "site_url",
                $"AND (kind = 'daily' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'daily' {searchConsoleWhere})) {searchConsoleWhere}", coverageParameters);

            var installsTotal = ScalarLong(connection,
                "SELECT COALESCE(SUM(installs), 0) FROM play_installs WHERE date >= $a AND date <= $b",
                ("$a", startStr), ("$b", endStr));
            var installsPrev = ScalarLong(connection,
                "SELECT COALESCE(SUM(installs), 0) FROM play_installs WHERE date >= $a AND date < $b",
                ("$a", prevStartStr), ("$b", startStr));

            var revenueTotal = ScalarDouble(connection,
                "SELECT COALESCE(SUM(revenue), 0.0) FROM play_revenue WHERE date >= $a AND date <= $b",
                ("$a", startStr), ("$b", endStr));
            var revenuePrev = ScalarDouble(connection,
                "SELECT COALESCE(SUM(revenue), 0.0) FROM play_revenue WHERE date >= $a AND date < $b",
                ("$a", prevStartStr), ("$b", startStr));

            var avgRating = ScalarDouble(connection,
                "SELECT COALESCE(average_rating, 0.0) FROM play_ratings WHERE package_name = $package ORDER BY date DESC LIMIT 1",
                ("$package", Domains.PlayStorePackage));
            var prevRating = ScalarDoubleOr(connection,
                "SELECT COALESCE(average_rating, 0.0) FROM play_ratings WHERE package_name = $package AND date < $a ORDER BY date DESC LIMIT 1",
                avgRating,
                ("$package", Domains.PlayStorePackage),
                ("$a", startStr));

            var crashRate = ScalarDouble(connection,
                "SELECT COALESCE(AVG(crash_rate), 0.0) FROM play_crashes WHERE date >= $a AND date <= $b",
                ("$a", startStr), ("$b", endStr));
            var crashRatePrev = ScalarDouble(connection,
                "SELECT COALESCE(AVG(crash_rate), 0.0) FROM play_crashes WHERE date >= $a AND date < $b",
                ("$a", prevStartStr), ("$b", startStr));

            return new SummaryData
            {
                VisitorsTotal = visitors.Total,
                VisitorsDays = visitors.Days,
                VisitorsLatestDate = visitors.LatestDate ?? "",
                VisitorsChangePct = CompletePeriodChange(visitorsPrev.Total, visitors.Total, visitorsPrev.Days, visitors.Days, days, visitorsComplete),
                ClicksTotal = clicks.Total,
                ClicksDays = clicks.Days,
                ClicksLatestDate = clicks.LatestDate ?? "",
                ClicksChangePct = CompletePeriodChange(clicksPrev.Total, clicks.Total, clicksPrev.Days, clicks.Days, days, clicksComplete),
                InstallsTotal = installsTotal,
                InstallsChangePct = PctChange(installsPrev, installsTotal),
                RevenueTotal = Math.Round(revenueTotal * 100.0) / 100.0,
                RevenueChangePct = PctChange(revenuePrev, revenueTotal),
                AverageRating = Math.Round(avgRating * 10.0) / 10.0,
                RatingChange = Math.Round((avgRating - prevRating) * 10.0) / 10.0,
                CrashRate = Math.Round(crashRate * 100.0) / 100.0,
                CrashRateChange = Math.Round((crashRate - crashRatePrev) * 100.0) / 100.0,
            };
        });
    }

    public Task<BingOverviewSummary> GetBingOverviewAsync(string domainOrAll, int days)
    {
        return _db.ReadAsync(connection =>
        {
            var (startStr, endStr, prevStartStr) = DateWindow(days);
            var filter = BingSiteFilter(domainOrAll);
            var currentSql = $"""
                SELECT COALESCE(SUM(clicks), 0) AS Clicks,
                       COALESCE(SUM(impressions), 0) AS Impressions,
                       COUNT(DISTINCT date) AS Days, MAX(date) AS LatestDate
                FROM bing_rank_traffic
                WHERE date >= @start AND date <= @end
                  {filter.WhereClause}
                """;
            var previousSql = $"""
                SELECT COALESCE(SUM(clicks), 0) AS Clicks, COUNT(DISTINCT date) AS Days
                FROM bing_rank_traffic
                WHERE date >= @prevStart AND date < @start
                  {filter.WhereClause}
                """;
            filter.Parameters.Add("start", startStr);
            filter.Parameters.Add("end", endStr);
            filter.Parameters.Add("prevStart", prevStartStr);
            filter.Parameters.Add("days", days);

            var current = connection.QuerySingle<(long Clicks, long Impressions, int Days, string? LatestDate)>(currentSql, filter.Parameters);
            var previous = connection.QuerySingle<(long Clicks, int Days)>(previousSql, filter.Parameters);
            return new BingOverviewSummary
            {
                Clicks = current.Clicks,
                PreviousClicks = previous.Clicks,
                Impressions = current.Impressions,
                Days = current.Days,
                LatestDate = current.LatestDate ?? "",
                ClicksChangePct = CompletePeriodChange(previous.Clicks, current.Clicks, previous.Days, current.Days, days,
                    HasCompleteSitePeriods(connection, "bing_rank_traffic", "site_url", filter.WhereClause, filter.Parameters)),
            };
        });
    }

    public Task<WebVitalsOverviewSummary> GetWebVitalsOverviewAsync(string domainOrAll)
    {
        return _db.ReadAsync(connection =>
        {
            var filter = CruxTargetFilter(domainOrAll);
            var sql = $"""
                WITH ranked AS (
                    SELECT metric,
                           p75,
                           ROW_NUMBER() OVER (
                               PARTITION BY target_type, target, form_factor, metric
                               ORDER BY collection_end DESC
                           ) AS rn
                    FROM crux_metric_points
                    WHERE metric IN ('largest_contentful_paint', 'interaction_to_next_paint', 'cumulative_layout_shift')
                      {filter.WhereClause}
                )
                SELECT metric AS Metric, p75 AS P75
                FROM ranked
                WHERE rn = 1
                """;
            var rows = connection.Query<(string Metric, double? P75)>(sql, filter.Parameters).ToList();
            if (rows.Count == 0)
            {
                return new WebVitalsOverviewSummary();
            }

            var statuses = rows.Select(row => PerformanceRepository.CruxStatus(row.Metric, row.P75)).ToList();
            var status = "No data";
            if (statuses.Contains("Fail", StringComparer.Ordinal)) status = "Fail";
            else if (statuses.Contains("Warn", StringComparer.Ordinal)) status = "Warn";
            else if (statuses.Contains("Pass", StringComparer.Ordinal)) status = "Pass";
            return new WebVitalsOverviewSummary
            {
                Status = status,
                Detail = $"{rows.Count(row => row.Metric == "largest_contentful_paint")} LCP, {rows.Count(row => row.Metric == "interaction_to_next_paint")} INP, {rows.Count(row => row.Metric == "cumulative_layout_shift")} CLS",
            };
        });
    }

    public Task<PageSpeedOverviewSummary> GetPageSpeedOverviewAsync(string domainOrAll)
    {
        return _db.ReadAsync(connection =>
        {
            var filter = PageSpeedUrlFilter(domainOrAll);
            var sql = $"""
                SELECT performance_score AS MobilePerformanceScore,
                       analysis_utc AS AnalysisUtc
                FROM pagespeed_runs
                WHERE strategy = 'MOBILE'
                  {filter.WhereClause}
                ORDER BY analysis_utc DESC
                LIMIT 1
                """;
            return connection.QuerySingleOrDefault<PageSpeedOverviewSummary>(sql, filter.Parameters) ?? new PageSpeedOverviewSummary();
        });
    }

    public Task<string> GetLatestSyncAsync()
    {
        return _db.ReadAsync(connection =>
            connection.ExecuteScalar<string?>(
                """
                SELECT COALESCE(MAX(last_sync), '')
                FROM connections
                WHERE id IN ('cf', 'sc', 'bing', 'perf', 'crux', 'pagespeed') OR source = 'cloudflare'
                """) ?? "");
    }

    private static long ScalarLong(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
        => connection.ExecuteScalar<long>(sql, ToParameters(parameters));

    private static double ScalarDouble(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
        => connection.ExecuteScalar<double>(sql, ToParameters(parameters));

    private static double ScalarDoubleOr(SqliteConnection connection, string sql, double fallback, params (string Name, object Value)[] parameters)
    {
        var result = connection.ExecuteScalar<double?>(sql, ToParameters(parameters));
        return result ?? fallback;
    }

    private static DynamicParameters ToParameters(params (string Name, object Value)[] parameters)
    {
        var dynamicParameters = new DynamicParameters();
        foreach (var (name, value) in parameters)
        {
            dynamicParameters.Add(name.TrimStart('$', '@', ':'), value);
        }
        return dynamicParameters;
    }

    private static double PctChange(double prev, double current)
    {
        if (prev == 0.0)
        {
            return current > 0.0 ? 100.0 : 0.0;
        }
        return Math.Round(((current - prev) / prev) * 1000.0) / 10.0;
    }

    private static double? CompletePeriodChange(long previous, long current, int previousDays, int currentDays, int days, bool completeSites)
        => completeSites && previousDays == days && currentDays == days && previous > 0
            ? PctChange(previous, current) : null;

    private static bool HasCompleteSitePeriods(SqliteConnection connection, string table, string identity,
        string filter, DynamicParameters parameters)
        => connection.ExecuteScalar<bool>(
            $"""
            SELECT COUNT(*) > 0 AND MIN(CurrentDays) = @days AND MIN(PreviousDays) = @days
            FROM (
                SELECT COUNT(DISTINCT CASE WHEN date >= @start THEN date END) AS CurrentDays,
                       COUNT(DISTINCT CASE WHEN date < @start THEN date END) AS PreviousDays
                FROM {table}
                WHERE date >= @prevStart AND date <= @end
                  {filter}
                GROUP BY {identity}
            )
            """, parameters);

    private static (string Start, string End, string PreviousStart) DateWindow(int days)
    {
        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var prevStart = startDate.AddDays(-days);
        string s(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (s(startDate), s(endDate), s(prevStart));
    }

    private static (string WhereClause, DynamicParameters Parameters) BingSiteFilter(string domainOrAll)
    {
        var parameters = new DynamicParameters();
        if (string.Equals(domainOrAll, "all", StringComparison.OrdinalIgnoreCase))
        {
            return ("", parameters);
        }

        parameters.Add("siteUrl", SiteIdentity.NormalizeHomePageUrl(domainOrAll));
        return ("AND site_url LIKE @siteUrl || '%'", parameters);
    }

    private static string NormalizeSummaryDomain(string domainOrAll)
        => IsAll(domainOrAll) ? "" : SiteIdentity.NormalizeDomain(domainOrAll);

    private static string SummaryDomainWhereClause(string domainOrAll, string column)
        => IsAll(domainOrAll) ? "" : $"AND {column} = @domain";

    private static bool IsAll(string domainOrAll)
        => string.Equals(domainOrAll, "all", StringComparison.OrdinalIgnoreCase);

    private static (string WhereClause, DynamicParameters Parameters) CruxTargetFilter(string domainOrAll)
    {
        var parameters = new DynamicParameters();
        if (IsAll(domainOrAll))
        {
            return ("", parameters);
        }

        var identity = SiteIdentity.FromDomainOrUrl(domainOrAll);
        parameters.Add("origin", identity.OriginUrl);
        parameters.Add("urlPrefix", identity.OriginUrl + "/%");
        return ("AND (target = @origin OR target LIKE @urlPrefix)", parameters);
    }

    private static (string WhereClause, DynamicParameters Parameters) PageSpeedUrlFilter(string domainOrAll)
    {
        var parameters = new DynamicParameters();
        if (IsAll(domainOrAll))
        {
            return ("", parameters);
        }

        var identity = SiteIdentity.FromDomainOrUrl(domainOrAll);
        parameters.Add("homePageUrl", identity.HomePageUrl);
        parameters.Add("urlPrefix", identity.OriginUrl + "/%");
        return ("AND (url = @homePageUrl OR url LIKE @urlPrefix)", parameters);
    }
}
