using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;

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

    public Task<List<WebAnalyticsSite>> ListSitesAsync()
    {
        return _db.ReadAsync(connection =>
            connection.Query<WebAnalyticsSite>(
                """
                SELECT domain AS Host, site_tag AS SiteTag
                FROM web_analytics_sites
                ORDER BY domain
                """).AsList()
        );
    }

    public Task<List<(string Domain, string SiteTag)>> ListSiteTagsAsync()
    {
        return _db.ReadAsync(connection =>
            connection.Query<(string Domain, string SiteTag)>(
                "SELECT domain AS Domain, site_tag AS SiteTag FROM web_analytics_sites").AsList()
        );
    }

    public Task SaveSitesAsync(IReadOnlyCollection<WebAnalyticsSite> sites, string discoveredAt)
    {
        return _db.WriteAsync(connection =>
        {
            foreach (var site in sites)
            {
                connection.Execute(
                    """
                    INSERT INTO web_analytics_sites (domain, site_tag, discovered_at)
                    VALUES (@domain, @siteTag, @discoveredAt)
                    ON CONFLICT(domain) DO UPDATE SET site_tag = excluded.site_tag
                    """,
                    new { domain = site.Host, siteTag = site.SiteTag, discoveredAt });
            }
        });
    }

    public Task DeleteSiteAsync(string domain)
    {
        return _db.WriteAsync(connection =>
        {
            connection.Execute("DELETE FROM web_analytics_sites WHERE domain = @domain", new { domain });
        });
    }

    public Task<long> UpsertRollupAsync(string domain, string rollupDate, WebAnalyticsRollup rollup, string fetchedAt)
    {
        return _db.WriteAsync(connection =>
        {
            long records = 0;
            foreach (var d in rollup.Daily)
            {
                connection.Execute(
                    """
                    INSERT INTO web_analytics_daily (domain, date, visits, page_views, fetched_at)
                    VALUES (@domain, @date, @visits, @pageViews, @fetchedAt)
                    ON CONFLICT(domain, date) DO UPDATE SET
                        visits = excluded.visits,
                        page_views = excluded.page_views,
                        fetched_at = excluded.fetched_at
                    """,
                    new { domain, date = d.Date, visits = d.Visits, pageViews = d.PageViews, fetchedAt });
                records++;
            }

            connection.Execute("DELETE FROM web_analytics_referrers WHERE domain = @domain AND date = @date",
                new { domain, date = rollupDate });
            foreach (var r in rollup.Referrers)
            {
                connection.Execute(
                    """
                    INSERT INTO web_analytics_referrers (domain, date, referrer, visits)
                    VALUES (@domain, @date, @referrer, @visits)
                    ON CONFLICT(domain, date, referrer) DO UPDATE SET visits = excluded.visits
                    """,
                    new { domain, date = rollupDate, referrer = r.Key, visits = r.Visits });
                records++;
            }

            connection.Execute("DELETE FROM web_analytics_pages WHERE domain = @domain AND date = @date",
                new { domain, date = rollupDate });
            foreach (var p in rollup.Pages)
            {
                connection.Execute(
                    """
                    INSERT INTO web_analytics_pages (domain, date, path, page_views)
                    VALUES (@domain, @date, @path, @pageViews)
                    ON CONFLICT(domain, date, path) DO UPDATE SET page_views = excluded.page_views
                    """,
                    new { domain, date = rollupDate, path = p.Path, pageViews = p.PageViews });
                records++;
            }

            connection.Execute("DELETE FROM web_analytics_countries WHERE domain = @domain AND date = @date",
                new { domain, date = rollupDate });
            foreach (var c in rollup.Countries)
            {
                connection.Execute(
                    """
                    INSERT INTO web_analytics_countries (domain, date, country, visits)
                    VALUES (@domain, @date, @country, @visits)
                    ON CONFLICT(domain, date, country) DO UPDATE SET visits = excluded.visits
                    """,
                    new { domain, date = rollupDate, country = c.Key, visits = c.Visits });
                records++;
            }

            return records;
        });
    }
}
