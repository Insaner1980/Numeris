using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Database;
using Numeris.Services.Performance;

namespace Numeris.Services.Database.Repositories;

public sealed class PerformanceRepository
{
    private readonly SqliteDatabase _db;

    public PerformanceRepository(SqliteDatabase db) => _db = db;

    public Task<List<PerformanceUrlInfo>> ListUrlsAsync(bool enabledOnly = false)
        => _db.ReadAsync(connection =>
        {
            return connection.Query<PerformanceUrlInfo>(
                """
                SELECT id AS Id, url AS Url, origin AS Origin, source AS Source, enabled AS Enabled, created_at AS CreatedAt
                FROM performance_urls
                WHERE @enabledOnly = 0 OR enabled = 1
                ORDER BY origin, url
                """, new { enabledOnly }).AsList();
        });

    public Task<List<CruxMetricSummary>> GetLatestCruxCoreVitalsAsync(string domainOrAll)
        => _db.ReadAsync(connection =>
        {
            var filter = CruxFilter(domainOrAll);
            var sql = $"""
                WITH ranked AS (
                    SELECT target_type AS TargetType,
                           target AS Target,
                           form_factor AS FormFactor,
                           metric AS Metric,
                           collection_end AS LatestCollectionEnd,
                           p75 AS P75,
                           good_density AS GoodDensity,
                           needs_improvement_density AS NeedsImprovementDensity,
                           poor_density AS PoorDensity,
                           ROW_NUMBER() OVER (
                               PARTITION BY target_type, target, form_factor, metric
                               ORDER BY collection_end DESC
                           ) AS rn
                    FROM crux_metric_points
                    WHERE metric IN ('largest_contentful_paint', 'interaction_to_next_paint', 'cumulative_layout_shift')
                      {filter.WhereClause}
                )
                SELECT TargetType, Target, FormFactor, Metric, LatestCollectionEnd, P75,
                       GoodDensity, NeedsImprovementDensity, PoorDensity
                FROM ranked
                WHERE rn = 1
                ORDER BY Target, FormFactor, Metric
                """;
            var rows = connection.Query<CruxMetricSummary>(sql, filter.Parameters).AsList();
            foreach (var row in rows)
            {
                row.Status = CruxStatus(row.Metric, row.P75);
            }
            return rows;
        });

    public Task<List<CruxTrendPoint>> GetCruxTrendAsync(string domainOrAll, string metric, string formFactor, string start, string end)
        => _db.ReadAsync(connection =>
        {
            var filter = CruxFilter(domainOrAll);
            var sql = $"""
                  SELECT collection_end AS CollectionEnd,
                         metric AS Metric,
                         form_factor AS FormFactor,
                         MAX(p75) AS P75
                  FROM crux_metric_points
                  WHERE metric = @metric
                    AND form_factor = @formFactor
                    AND collection_end >= @start
                    AND collection_end <= @end
                    {filter.WhereClause}
                  GROUP BY collection_end, metric, form_factor
                  ORDER BY collection_end
                  """;
            filter.Parameters.Add("metric", metric);
            filter.Parameters.Add("formFactor", formFactor);
            filter.Parameters.Add("start", start);
            filter.Parameters.Add("end", end);
            return connection.Query<CruxTrendPoint>(sql, filter.Parameters).AsList();
        });

    public Task<List<PageSpeedLatestRun>> GetLatestPageSpeedRunsAsync(string domainOrAll)
        => _db.ReadAsync(connection =>
        {
            var filter = PageSpeedFilter(domainOrAll);
            var sql = $"""
                WITH ranked AS (
                    SELECT url AS Url,
                           strategy AS Strategy,
                           analysis_utc AS AnalysisUtc,
                           final_url AS FinalUrl,
                           performance_score AS PerformanceScore,
                           accessibility_score AS AccessibilityScore,
                           best_practices_score AS BestPracticesScore,
                           seo_score AS SeoScore,
                           runtime_error AS RuntimeError,
                           ROW_NUMBER() OVER (
                               PARTITION BY url, strategy
                               ORDER BY analysis_utc DESC
                           ) AS rn
                    FROM pagespeed_runs
                    WHERE 1 = 1
                      {filter.WhereClause}
                )
                SELECT Url, Strategy, AnalysisUtc, FinalUrl, PerformanceScore,
                       AccessibilityScore, BestPracticesScore, SeoScore, RuntimeError
                FROM ranked
                WHERE rn = 1
                ORDER BY Url, Strategy
                """;
            return connection.Query<PageSpeedLatestRun>(sql, filter.Parameters).AsList();
        });

    public Task<List<PageSpeedScorePoint>> GetPageSpeedScoreTrendAsync(string domainOrAll, string start, string end)
        => _db.ReadAsync(connection =>
        {
            var filter = PageSpeedFilter(domainOrAll);
            var aggregate = IsAll(domainOrAll) ? "MIN(performance_score)" : "AVG(performance_score)";
            var sql = $"""
                SELECT analysis_utc AS AnalysisUtc,
                       strategy AS Strategy,
                       {aggregate} AS PerformanceScore
                FROM pagespeed_runs
                WHERE analysis_utc >= @start
                  AND analysis_utc <= @end
                  {filter.WhereClause}
                GROUP BY analysis_utc, strategy
                ORDER BY analysis_utc, strategy
                """;
            filter.Parameters.Add("start", start);
            filter.Parameters.Add("end", end);
            return connection.Query<PageSpeedScorePoint>(sql, filter.Parameters).AsList();
        });

    public Task<List<PageSpeedAuditIssue>> GetPageSpeedAuditIssuesAsync(string domainOrAll, int limit)
        => _db.ReadAsync(connection =>
        {
            var filter = PageSpeedFilter(domainOrAll);
            var sql = $"""
                WITH latest AS (
                    SELECT url, strategy, MAX(analysis_utc) AS analysis_utc
                    FROM pagespeed_runs
                    WHERE 1 = 1
                      {filter.WhereClause}
                    GROUP BY url, strategy
                )
                SELECT a.url AS Url,
                       a.strategy AS Strategy,
                       a.audit_id AS AuditId,
                       COALESCE(a.title, a.audit_id) AS Title,
                       a.score AS Score,
                       COALESCE(a.display_value, '') AS DisplayValue,
                       a.numeric_value AS NumericValue,
                       COALESCE(a.numeric_unit, '') AS NumericUnit
                FROM pagespeed_audits a
                INNER JOIN latest l
                    ON l.url = a.url
                   AND l.strategy = a.strategy
                   AND l.analysis_utc = a.analysis_utc
                WHERE a.score IS NOT NULL
                  AND a.score < 0.9
                ORDER BY a.score ASC, a.numeric_value DESC
                LIMIT @limit
                """;
            filter.Parameters.Add("limit", limit);
            return connection.Query<PageSpeedAuditIssue>(sql, filter.Parameters).AsList();
        });

    public Task AddUrlAsync(string url, string source = "manual")
        => _db.WriteAsync(connection =>
        {
            var pageUrl = PerformanceUrl.NormalizePageUrl(url);
            var origin = PerformanceUrl.NormalizeOrigin(pageUrl);
            connection.Execute(
                """
                INSERT INTO performance_urls (url, origin, source, enabled, created_at)
                VALUES (@pageUrl, @origin, @source, 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'))
                ON CONFLICT(url) DO UPDATE SET origin = excluded.origin, source = excluded.source, enabled = 1
                """,
                new { pageUrl, origin, source });
        });

    public Task DeleteUrlAsync(long id)
        => _db.WriteAsync(connection =>
        {
            connection.Execute("DELETE FROM performance_urls WHERE id = @id", new { id });
        });

    public Task UpsertCruxMetricAsync(CruxMetricPoint row)
        => _db.WriteAsync(connection =>
        {
            row.RawJson = RawJsonStoragePolicy.TrimRawJson(row.RawJson);
            connection.Execute(
                """
                INSERT INTO crux_metric_points
                (target_type, target, form_factor, collection_start, collection_end, metric, p75,
                 good_density, needs_improvement_density, poor_density, raw_json, fetched_at)
                VALUES (@TargetType, @Target, @FormFactor, @CollectionStart, @CollectionEnd, @Metric, @P75,
                        @GoodDensity, @NeedsImprovementDensity, @PoorDensity, @RawJson, @FetchedAt)
                ON CONFLICT(target_type, target, form_factor, collection_end, metric) DO UPDATE SET
                    collection_start = excluded.collection_start,
                    p75 = excluded.p75,
                    good_density = excluded.good_density,
                    needs_improvement_density = excluded.needs_improvement_density,
                    poor_density = excluded.poor_density,
                    raw_json = excluded.raw_json,
                    fetched_at = excluded.fetched_at
                """,
                row);
        });

    public Task UpsertPageSpeedRunAsync(PageSpeedRun run, IReadOnlyList<PageSpeedAudit> audits)
        => _db.WriteTransactionAsync((connection, transaction) =>
        {
            run.RawJson = RawJsonStoragePolicy.TrimRawJson(run.RawJson);
            run.RuntimeError = RawJsonStoragePolicy.TrimDetailsJson(run.RuntimeError);
            run.WarningsJson = RawJsonStoragePolicy.TrimDetailsJson(run.WarningsJson);
            connection.Execute(
                """
                INSERT INTO pagespeed_runs
                (url, strategy, analysis_utc, final_url, performance_score, accessibility_score, best_practices_score,
                 seo_score, lighthouse_version, runtime_error, warnings_json, raw_json, fetched_at)
                VALUES (@Url, @Strategy, @AnalysisUtc, @FinalUrl, @PerformanceScore, @AccessibilityScore, @BestPracticesScore,
                        @SeoScore, @LighthouseVersion, @RuntimeError, @WarningsJson, @RawJson, @FetchedAt)
                ON CONFLICT(url, strategy, analysis_utc) DO UPDATE SET
                    final_url = excluded.final_url,
                    performance_score = excluded.performance_score,
                    accessibility_score = excluded.accessibility_score,
                    best_practices_score = excluded.best_practices_score,
                    seo_score = excluded.seo_score,
                    lighthouse_version = excluded.lighthouse_version,
                    runtime_error = excluded.runtime_error,
                    warnings_json = excluded.warnings_json,
                    raw_json = excluded.raw_json,
                    fetched_at = excluded.fetched_at
                """,
                run,
                transaction);

            foreach (var audit in audits)
            {
                audit.DetailsJson = RawJsonStoragePolicy.TrimDetailsJson(audit.DetailsJson);
                connection.Execute(
                    """
                    INSERT INTO pagespeed_audits
                    (url, strategy, analysis_utc, audit_id, title, score, numeric_value, numeric_unit,
                     display_value, score_display_mode, details_json)
                    VALUES (@Url, @Strategy, @AnalysisUtc, @AuditId, @Title, @Score, @NumericValue, @NumericUnit,
                            @DisplayValue, @ScoreDisplayMode, @DetailsJson)
                    ON CONFLICT(url, strategy, analysis_utc, audit_id) DO UPDATE SET
                        title = excluded.title,
                        score = excluded.score,
                        numeric_value = excluded.numeric_value,
                        numeric_unit = excluded.numeric_unit,
                        display_value = excluded.display_value,
                        score_display_mode = excluded.score_display_mode,
                        details_json = excluded.details_json
                    """,
                    audit,
                    transaction);
            }
        });

    public Task ApplyPageSpeedRetentionAsync()
        => _db.WriteTransactionAsync((connection, transaction) =>
        {
            connection.Execute(
                """
                DELETE FROM pagespeed_audits
                WHERE (url, strategy, analysis_utc) IN (
                    SELECT url, strategy, analysis_utc
                    FROM (
                        SELECT url,
                               strategy,
                               analysis_utc,
                               ROW_NUMBER() OVER (
                                   PARTITION BY url, strategy
                                   ORDER BY analysis_utc DESC
                               ) AS rn
                        FROM pagespeed_runs
                    )
                    WHERE rn > @keep
                )
                """,
                new { keep = RawJsonStoragePolicy.PageSpeedRunsToKeepPerUrlAndStrategy },
                transaction);

            connection.Execute(
                """
                DELETE FROM pagespeed_runs
                WHERE (url, strategy, analysis_utc) IN (
                    SELECT url, strategy, analysis_utc
                    FROM (
                        SELECT url,
                               strategy,
                               analysis_utc,
                               ROW_NUMBER() OVER (
                                   PARTITION BY url, strategy
                                   ORDER BY analysis_utc DESC
                               ) AS rn
                        FROM pagespeed_runs
                    )
                    WHERE rn > @keep
                )
                """,
                new { keep = RawJsonStoragePolicy.PageSpeedRunsToKeepPerUrlAndStrategy },
                transaction);
        });

    private static (string WhereClause, DynamicParameters Parameters) CruxFilter(string domainOrAll)
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

    private static (string WhereClause, DynamicParameters Parameters) PageSpeedFilter(string domainOrAll)
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

    private static bool IsAll(string domainOrAll)
        => string.Equals(domainOrAll, "all", System.StringComparison.OrdinalIgnoreCase);

    public static string CruxStatus(string metric, double? p75)
    {
        if (!p75.HasValue)
        {
            return "No data";
        }

        var (good, poor) = metric switch
        {
            "largest_contentful_paint" => (2500.0, 4000.0),
            "interaction_to_next_paint" => (200.0, 500.0),
            "cumulative_layout_shift" => (0.10, 0.25),
            _ => (double.MaxValue, double.MaxValue),
        };

        if (p75.Value <= good) return "Pass";
        return p75.Value <= poor ? "Warn" : "Fail";
    }
}
