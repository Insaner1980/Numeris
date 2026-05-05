using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;

namespace Numeris.Services.Database.Repositories;

public sealed class WebAnalyticsRepository
{
    private readonly SqliteDatabase _db;

    public WebAnalyticsRepository(SqliteDatabase db) => _db = db;

    public Task<List<WebAnalyticsDay>> GetDailyAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT date AS Date,
                         SUM(visits) AS Visits,
                         SUM(page_views) AS PageViews
                  FROM web_analytics_daily
                  WHERE date >= @start AND date <= @end
                  GROUP BY date ORDER BY date
                  """
                : """
                  SELECT date AS Date,
                         visits AS Visits,
                         page_views AS PageViews
                  FROM web_analytics_daily
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  ORDER BY date
                  """;
            return new List<WebAnalyticsDay>(connection.Query<WebAnalyticsDay>(sql, new { domain, start, end }));
        });
    }

    public Task<List<WebAnalyticsReferrer>> GetReferrersAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT referrer AS Referrer, SUM(visits) AS Visits
                  FROM web_analytics_referrers
                  WHERE date >= @start AND date <= @end
                  GROUP BY referrer ORDER BY Visits DESC LIMIT 10
                  """
                : """
                  SELECT referrer AS Referrer, SUM(visits) AS Visits
                  FROM web_analytics_referrers
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  GROUP BY referrer ORDER BY Visits DESC LIMIT 10
                  """;
            return new List<WebAnalyticsReferrer>(connection.Query<WebAnalyticsReferrer>(sql, new { domain, start, end }));
        });
    }

    public Task<List<WebAnalyticsPage>> GetPagesAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT path AS Path, SUM(page_views) AS PageViews
                  FROM web_analytics_pages
                  WHERE date >= @start AND date <= @end
                  GROUP BY path ORDER BY PageViews DESC LIMIT 10
                  """
                : """
                  SELECT path AS Path, SUM(page_views) AS PageViews
                  FROM web_analytics_pages
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  GROUP BY path ORDER BY PageViews DESC LIMIT 10
                  """;
            return new List<WebAnalyticsPage>(connection.Query<WebAnalyticsPage>(sql, new { domain, start, end }));
        });
    }

    public Task<List<CountryData>> GetCountriesAsync(string domain, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var sql = domain == "all"
                ? """
                  SELECT country AS Country, SUM(visits) AS Value
                  FROM web_analytics_countries
                  WHERE date >= @start AND date <= @end
                  GROUP BY country ORDER BY Value DESC LIMIT 10
                  """
                : """
                  SELECT country AS Country, SUM(visits) AS Value
                  FROM web_analytics_countries
                  WHERE domain = @domain AND date >= @start AND date <= @end
                  GROUP BY country ORDER BY Value DESC LIMIT 10
                  """;
            return new List<CountryData>(connection.Query<CountryData>(sql, new { domain, start, end }));
        });
    }
}
