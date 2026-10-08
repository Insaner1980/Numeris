using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;

namespace Numeris.Services.Database.Repositories;

public sealed class HealthRepository
{
    private readonly SqliteDatabase _db;

    public HealthRepository(SqliteDatabase db) => _db = db;

    public Task RecordUptimeProbeAsync(UptimeProbeResult probe)
    {
        return _db.WriteAsync(connection =>
        {
            var checkedTime = DateTime.Now;
            var latest = connection.ExecuteScalar<string>("SELECT MAX(checked_at) FROM uptime_checks WHERE domain = @Domain", new { probe.Domain });
            if (DateTime.TryParse(latest, CultureInfo.InvariantCulture, DateTimeStyles.None, out var previous) && checkedTime <= previous)
            {
                checkedTime = previous.AddTicks(1);
            }
            var checkedAt = checkedTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            connection.Execute(
                """
                INSERT INTO uptime_checks
                (domain, checked_at, status, status_code, response_ms, error_message)
                VALUES (@Domain, @CheckedAt, @Status, @StatusCode, @ResponseMs, @ErrorMessage)
                """,
                new
                {
                    probe.Domain,
                    CheckedAt = checkedAt,
                    probe.Status,
                    probe.StatusCode,
                    probe.ResponseMs,
                    ErrorMessage = ApiErrorMessage.Sanitize(probe.ErrorMessage),
                });
        });
    }

    public Task<List<UptimeDomainStatus>> GetUptimeStatusAsync(string domain)
    {
        return _db.ReadAsync(connection =>
        {
            var domains = domain == "all"
                ? new[] { Domains.KnitTools, Domains.Finnvek }
                : new[] { domain };

            var result = new List<UptimeDomainStatus>();
            foreach (var d in domains)
            {
                var latest = connection.QueryFirstOrDefault<(string CheckedAt, string Status, int? Code, long? Ms)>(
                    """
                    SELECT checked_at, status, status_code AS Code, response_ms AS Ms
                    FROM uptime_checks WHERE domain = @d ORDER BY checked_at DESC LIMIT 1
                    """,
                    new { d });

                var (total, ok, incidents) = connection.QueryFirstOrDefault<(long Total, long Ok, long Incidents)>(
                    """
                    SELECT COUNT(*) AS Total,
                           COALESCE(SUM(CASE WHEN status = 'up' THEN 1 ELSE 0 END), 0) AS Ok,
                           COALESCE(SUM(CASE WHEN status != 'up' THEN 1 ELSE 0 END), 0) AS Incidents
                    FROM uptime_checks WHERE domain = @d
                    """,
                    new { d });

                var pct = total > 0 ? (double)ok / total * 100.0 : 0.0;

                result.Add(new UptimeDomainStatus
                {
                    Domain = d,
                    Status = latest.Status ?? "pending",
                    LastCheckedAt = latest.CheckedAt,
                    StatusCode = latest.Code,
                    ResponseMs = latest.Ms,
                    UptimePct = pct,
                    Incidents = incidents,
                });
            }
            return result;
        });
    }

    public Task<List<UptimeCheckDay>> GetUptimeDailyAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT domain AS Domain,
                         substr(checked_at, 1, 10) AS Date,
                         AVG(CASE WHEN status = 'up' THEN 100.0 ELSE 0.0 END) AS UptimePct,
                         AVG(response_ms) AS AvgResponseMs,
                         SUM(CASE WHEN status != 'up' THEN 1 ELSE 0 END) AS Incidents
                  FROM uptime_checks
                  WHERE substr(checked_at, 1, 10) BETWEEN @start AND @end
                  GROUP BY domain, Date ORDER BY Date, domain
                  """
                : """
                  SELECT domain AS Domain,
                         substr(checked_at, 1, 10) AS Date,
                         AVG(CASE WHEN status = 'up' THEN 100.0 ELSE 0.0 END) AS UptimePct,
                         AVG(response_ms) AS AvgResponseMs,
                         SUM(CASE WHEN status != 'up' THEN 1 ELSE 0 END) AS Incidents
                  FROM uptime_checks
                  WHERE domain = @domain AND substr(checked_at, 1, 10) BETWEEN @start AND @end
                  GROUP BY domain, Date ORDER BY Date
                  """;
            return new List<UptimeCheckDay>(connection.Query<UptimeCheckDay>(sql, new { domain, start, end }));
        });
    }

    public async Task<SitemapRefreshResult> RefreshSitemapAsync(string domain, IReadOnlyList<string> liveUrls)
    {
        SitemapRefreshResult result = null!;
        await _db.WriteTransactionAsync((connection, transaction) =>
        {
            var nowStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var existingActive = new HashSet<string>(connection.Query<string>(
                "SELECT url FROM sitemap_urls WHERE domain = @domain AND removed_at IS NULL",
                new { domain }, transaction));

            var liveSet = new HashSet<string>(liveUrls);
            long newCount = 0;
            foreach (var url in liveSet)
            {
                if (existingActive.Contains(url))
                {
                    connection.Execute(
                        "UPDATE sitemap_urls SET last_seen_at = @nowStr, removed_at = NULL WHERE domain = @domain AND url = @url",
                        new { nowStr, domain, url }, transaction);
                }
                else
                {
                    connection.Execute(
                        """
                        INSERT OR REPLACE INTO sitemap_urls (domain, url, discovered_at, last_seen_at, removed_at)
                        VALUES (@domain, @url, @nowStr, @nowStr, NULL)
                        """,
                        new { domain, url, nowStr }, transaction);
                    newCount++;
                }
            }

            long removedCount = 0;
            foreach (var oldUrl in existingActive.Where(oldUrl => !liveSet.Contains(oldUrl)))
            {
                connection.Execute(
                    "UPDATE sitemap_urls SET removed_at = @nowStr WHERE domain = @domain AND url = @url",
                    new { nowStr, domain, url = oldUrl }, transaction);
                removedCount++;
            }

            result = new SitemapRefreshResult
            {
                Domain = domain,
                TotalUrls = liveSet.Count,
                NewUrls = newCount,
                RemovedUrls = removedCount,
            };
        }).ConfigureAwait(false);
        return result;
    }
}
