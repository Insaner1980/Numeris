using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;

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
}
