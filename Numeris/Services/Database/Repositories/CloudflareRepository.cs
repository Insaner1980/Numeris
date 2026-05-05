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
}
