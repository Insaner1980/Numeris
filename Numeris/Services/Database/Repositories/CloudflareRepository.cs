using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;

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
                ? "SELECT COALESCE(SUM(pageviews), 1) FROM cloudflare_traffic WHERE date >= @start AND date <= @end"
                : "SELECT COALESCE(SUM(pageviews), 1) FROM cloudflare_traffic WHERE domain = @domain AND date >= @start AND date <= @end";
            var total = connection.ExecuteScalar<long>(totalSql, new { domain, start, end });
            if (total <= 0) total = 1;

            var sql = domain == "all"
                ? """
                  SELECT top_path AS Path, SUM(pageviews) AS Pageviews
                  FROM cloudflare_traffic
                  WHERE date >= @start AND date <= @end AND top_path IS NOT NULL
                  GROUP BY top_path ORDER BY Pageviews DESC LIMIT 10
                  """
                : """
                  SELECT top_path AS Path, SUM(pageviews) AS Pageviews
                  FROM cloudflare_traffic
                  WHERE domain = @domain AND date >= @start AND date <= @end AND top_path IS NOT NULL
                  GROUP BY top_path ORDER BY Pageviews DESC LIMIT 10
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
}
