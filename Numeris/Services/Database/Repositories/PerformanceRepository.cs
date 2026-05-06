using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Performance;

namespace Numeris.Services.Database.Repositories;

public sealed class PerformanceRepository
{
    private readonly SqliteDatabase _db;

    public PerformanceRepository(SqliteDatabase db) => _db = db;

    public Task<List<PerformanceUrlInfo>> ListUrlsAsync(bool enabledOnly = false)
        => _db.ReadAsync(connection =>
        {
            var where = enabledOnly ? "WHERE enabled = 1" : "";
            return connection.Query<PerformanceUrlInfo>(
                $"""
                SELECT id AS Id, url AS Url, origin AS Origin, source AS Source, enabled AS Enabled, created_at AS CreatedAt
                FROM performance_urls
                {where}
                ORDER BY origin, url
                """).AsList();
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
        => _db.WriteAsync(connection =>
        {
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
                run);

            foreach (var audit in audits)
            {
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
                    audit);
            }
        });
}
