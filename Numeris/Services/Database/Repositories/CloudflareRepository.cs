using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;

namespace Numeris.Services.Database.Repositories;

public sealed class CloudflareRepository
{
    private readonly SqliteDatabase _db;

    public CloudflareRepository(SqliteDatabase db) => _db = db;

    public Task<List<TrafficDay>> GetTrafficDailyAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT date AS Date,
                         SUM(pageviews) AS Pageviews,
                         SUM(unique_visitors) AS UniqueVisitors
                  FROM cloudflare_traffic
                  WHERE date >= @start AND date <= @end
                  GROUP BY date ORDER BY date
                  """
                : """
                  SELECT date AS Date,
                         pageviews AS Pageviews,
                         unique_visitors AS UniqueVisitors
                  FROM cloudflare_traffic
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  ORDER BY date
                  """;

            return new List<TrafficDay>(connection.Query<TrafficDay>(sql, new { domain, start, end }));
        });
    }

    public Task<List<CacheDay>> GetCacheDailyAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT date AS Date,
                         SUM(cached_requests) AS CachedRequests,
                         SUM(requests) AS TotalRequests,
                         SUM(cached_bytes) AS CachedBytes,
                         SUM(total_bytes) AS TotalBytes
                  FROM cloudflare_traffic
                  WHERE date >= @start AND date <= @end
                  GROUP BY date ORDER BY date
                  """
                : """
                  SELECT date AS Date,
                         cached_requests AS CachedRequests,
                         requests AS TotalRequests,
                         cached_bytes AS CachedBytes,
                         total_bytes AS TotalBytes
                  FROM cloudflare_traffic
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  ORDER BY date
                  """;

            var rows = new List<CacheDay>(connection.Query<CacheDay>(sql, new { domain, start, end }));
            foreach (var row in rows)
            {
                row.HitRatio = row.TotalRequests > 0
                    ? (double)row.CachedRequests / row.TotalRequests
                    : 0.0;
            }
            return rows;
        });
    }

    public Task<List<SecurityDay>> GetSecurityDailyAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT date AS Date,
                         SUM(threats) AS Threats,
                         SUM(requests) AS TotalRequests
                  FROM cloudflare_traffic
                  WHERE date >= @start AND date <= @end
                  GROUP BY date ORDER BY date
                  """
                : """
                  SELECT date AS Date,
                         threats AS Threats,
                         requests AS TotalRequests
                  FROM cloudflare_traffic
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  ORDER BY date
                  """;

            return new List<SecurityDay>(connection.Query<SecurityDay>(sql, new { domain, start, end }));
        });
    }

    public Task<List<CountryData>> GetTrafficCountriesAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT country AS Country, SUM(visitors) AS Value
                  FROM cloudflare_countries
                  WHERE date >= @start AND date <= @end
                  GROUP BY country ORDER BY Value DESC LIMIT 10
                  """
                : """
                  SELECT country AS Country, SUM(visitors) AS Value
                  FROM cloudflare_countries
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  GROUP BY country ORDER BY Value DESC LIMIT 10
                  """;
            return new List<CountryData>(connection.Query<CountryData>(sql, new { domain, start, end }));
        });
    }

    public Task<List<PageData>> GetTrafficPagesAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var totalSql = domain == "all"
                ? "SELECT COALESCE(SUM(requests), 1) FROM cloudflare_pages WHERE date >= @start AND date <= @end"
                : "SELECT COALESCE(SUM(requests), 1) FROM cloudflare_pages WHERE domain = @domain AND date >= @start AND date <= @end";
            var total = connection.ExecuteScalar<long>(totalSql, new { domain, start, end });
            if (total <= 0) total = 1;

            var sql = domain == "all"
                ? """
                  SELECT path AS Path, SUM(requests) AS Pageviews
                  FROM cloudflare_pages
                  WHERE date >= @start AND date <= @end
                  GROUP BY path ORDER BY Pageviews DESC LIMIT 10
                  """
                : """
                  SELECT path AS Path, SUM(requests) AS Pageviews
                  FROM cloudflare_pages
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  GROUP BY path ORDER BY Pageviews DESC LIMIT 10
                  """;
            var rows = new List<PageData>(connection.Query<PageData>(sql, new { domain, start, end }));
            foreach (var row in rows)
            {
                row.Percentage = System.Math.Round((double)row.Pageviews / total * 1000.0) / 10.0;
            }
            return rows;
        });
    }

    public Task<List<StatusCodeDay>> GetStatusCodesDailyAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT date AS Date, status_code AS StatusCode, SUM(requests) AS Requests
                  FROM cloudflare_status_codes
                  WHERE date >= @start AND date <= @end
                  GROUP BY date, status_code ORDER BY date, status_code
                  """
                : """
                  SELECT date AS Date, status_code AS StatusCode, requests AS Requests
                  FROM cloudflare_status_codes
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  ORDER BY date, status_code
                  """;
            return new List<StatusCodeDay>(connection.Query<StatusCodeDay>(sql, new { domain, start, end }));
        });
    }

    public async Task<long> UpsertTrafficAsync(string domain, CloudflareTrafficResult traffic, string fetchedAt)
    {
        long count = 0;
        await _db.WriteTransactionAsync((connection, transaction) =>
        {
            foreach (var row in traffic.Daily)
            {
                connection.Execute(
                    """
                    INSERT INTO cloudflare_traffic
                    (domain, date, pageviews, unique_visitors, requests, cached_requests,
                     cached_bytes, total_bytes, threats, top_country, top_path, fetched_at)
                    VALUES (@domain, @date, @pageviews, @uniqueVisitors, @requests, @cachedRequests,
                            @cachedBytes, @totalBytes, @threats, NULL, NULL, @fetchedAt)
                    ON CONFLICT(domain, date) DO UPDATE SET
                        pageviews = excluded.pageviews,
                        unique_visitors = excluded.unique_visitors,
                        requests = excluded.requests,
                        cached_requests = excluded.cached_requests,
                        cached_bytes = excluded.cached_bytes,
                        total_bytes = excluded.total_bytes,
                        threats = excluded.threats,
                        fetched_at = excluded.fetched_at
                    """,
                    new
                    {
                        domain,
                        date = row.Date,
                        pageviews = row.Pageviews,
                        uniqueVisitors = row.UniqueVisitors,
                        requests = row.Requests,
                        cachedRequests = row.CachedRequests,
                        cachedBytes = row.CachedBytes,
                        totalBytes = row.TotalBytes,
                        threats = row.Threats,
                        fetchedAt,
                    },
                    transaction);
                count++;
            }

            foreach (var s in traffic.StatusCodes)
            {
                connection.Execute(
                    """
                    INSERT INTO cloudflare_status_codes (domain, date, status_code, requests)
                    VALUES (@domain, @date, @statusCode, @requests)
                    ON CONFLICT(domain, date, status_code) DO UPDATE SET requests = excluded.requests
                    """,
                    new { domain, date = s.Date, statusCode = s.StatusCode, requests = s.Requests },
                    transaction);
                count++;
            }

            if (!string.IsNullOrWhiteSpace(traffic.BreakdownDate))
            {
                connection.Execute(
                    "DELETE FROM cloudflare_countries WHERE domain = @domain AND date >= @start AND date <= @end",
                    new { domain, start = traffic.BreakdownStartDate, end = traffic.BreakdownDate },
                    transaction);
                connection.Execute(
                    "DELETE FROM cloudflare_pages WHERE domain = @domain AND date >= @start AND date <= @end",
                    new { domain, start = traffic.BreakdownStartDate, end = traffic.BreakdownDate },
                    transaction);
            }

            foreach (var c in traffic.Countries)
            {
                connection.Execute(
                    """
                    INSERT INTO cloudflare_countries (domain, date, country, visitors)
                    VALUES (@domain, @date, @country, @value)
                    ON CONFLICT(domain, date, country) DO UPDATE SET visitors = excluded.visitors
                    """,
                    new { domain, date = c.Date, country = c.Country, value = c.Value },
                    transaction);
                count++;
            }

            foreach (var p in traffic.Pages)
            {
                connection.Execute(
                    """
                    INSERT INTO cloudflare_pages (domain, date, path, requests)
                    VALUES (@domain, @date, @path, @value)
                    ON CONFLICT(domain, date, path) DO UPDATE SET requests = excluded.requests
                    """,
                    new { domain, date = p.Date, path = p.Path, value = p.Value },
                    transaction);
                count++;
            }
        }).ConfigureAwait(false);
        return count;
    }
}
