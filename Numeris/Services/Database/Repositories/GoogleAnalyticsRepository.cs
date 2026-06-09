using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Numeris.Helpers;
using Numeris.Models;

namespace Numeris.Services.Database.Repositories;

public sealed class GoogleAnalyticsRepository
{
    private readonly SqliteDatabase _db;

    public GoogleAnalyticsRepository(SqliteDatabase db) => _db = db;

    public async Task<long> UpsertRollupAsync(
        string domain,
        string propertyId,
        string periodStart,
        string periodEnd,
        GoogleAnalyticsRollup rollup,
        string fetchedAt)
    {
        domain = SiteIdentity.NormalizeDomain(domain);
        long records = 0;
        await _db.WriteTransactionAsync((connection, transaction) =>
        {
            connection.Execute(
                "DELETE FROM google_analytics_daily WHERE domain = @domain AND property_id = @propertyId AND date >= @periodStart AND date <= @periodEnd",
                new { domain, propertyId, periodStart, periodEnd },
                transaction);
            connection.Execute(
                "DELETE FROM google_analytics_pages WHERE domain = @domain AND property_id = @propertyId AND period_start = @periodStart AND period_end = @periodEnd",
                new { domain, propertyId, periodStart, periodEnd },
                transaction);
            connection.Execute(
                "DELETE FROM google_analytics_sources WHERE domain = @domain AND property_id = @propertyId AND period_start = @periodStart AND period_end = @periodEnd",
                new { domain, propertyId, periodStart, periodEnd },
                transaction);
            connection.Execute(
                "DELETE FROM google_analytics_events WHERE domain = @domain AND property_id = @propertyId AND period_start = @periodStart AND period_end = @periodEnd",
                new { domain, propertyId, periodStart, periodEnd },
                transaction);
            connection.Execute(
                "DELETE FROM google_analytics_devices WHERE domain = @domain AND property_id = @propertyId AND date >= @periodStart AND date <= @periodEnd",
                new { domain, propertyId, periodStart, periodEnd },
                transaction);

                foreach (var row in rollup.Daily)
                {
                    records += connection.Execute(
                        """
                        INSERT INTO google_analytics_daily
                        (domain, property_id, date, active_users, sessions, page_views, engaged_sessions, event_count, engagement_rate, fetched_at)
                        VALUES (@domain, @propertyId, @date, @activeUsers, @sessions, @pageViews, @engagedSessions, @eventCount, @engagementRate, @fetchedAt)
                        """,
                        new
                        {
                            domain,
                            propertyId,
                            date = row.Date,
                            activeUsers = row.ActiveUsers,
                            sessions = row.Sessions,
                            pageViews = row.PageViews,
                            engagedSessions = row.EngagedSessions,
                            eventCount = row.EventCount,
                            engagementRate = row.EngagementRate,
                            fetchedAt,
                        },
                        transaction);
                }

                foreach (var row in rollup.Pages)
                {
                    records += connection.Execute(
                        """
                        INSERT INTO google_analytics_pages
                        (domain, property_id, period_start, period_end, page_path, active_users, sessions, page_views, engaged_sessions, engagement_rate, fetched_at)
                        VALUES (@domain, @propertyId, @periodStart, @periodEnd, @pagePath, @activeUsers, @sessions, @pageViews, @engagedSessions, @engagementRate, @fetchedAt)
                        """,
                        new
                        {
                            domain,
                            propertyId,
                            periodStart,
                            periodEnd,
                            pagePath = row.PagePath,
                            activeUsers = row.ActiveUsers,
                            sessions = row.Sessions,
                            pageViews = row.PageViews,
                            engagedSessions = row.EngagedSessions,
                            engagementRate = row.EngagementRate,
                            fetchedAt,
                        },
                        transaction);
                }

                foreach (var row in rollup.Sources)
                {
                    records += connection.Execute(
                        """
                        INSERT INTO google_analytics_sources
                        (domain, property_id, period_start, period_end, source_medium, sessions, active_users, key_events, fetched_at)
                        VALUES (@domain, @propertyId, @periodStart, @periodEnd, @sourceMedium, @sessions, @activeUsers, @keyEvents, @fetchedAt)
                        """,
                        new
                        {
                            domain,
                            propertyId,
                            periodStart,
                            periodEnd,
                            sourceMedium = row.SourceMedium,
                            sessions = row.Sessions,
                            activeUsers = row.ActiveUsers,
                            keyEvents = row.KeyEvents,
                            fetchedAt,
                        },
                        transaction);
                }

                foreach (var row in rollup.Events)
                {
                    records += connection.Execute(
                        """
                        INSERT INTO google_analytics_events
                        (domain, property_id, period_start, period_end, event_name, event_count, key_events, fetched_at)
                        VALUES (@domain, @propertyId, @periodStart, @periodEnd, @eventName, @eventCount, @keyEvents, @fetchedAt)
                        """,
                        new
                        {
                            domain,
                            propertyId,
                            periodStart,
                            periodEnd,
                            eventName = row.EventName,
                            eventCount = row.EventCount,
                            keyEvents = row.KeyEvents,
                            fetchedAt,
                        },
                        transaction);
                }

                foreach (var row in rollup.Devices)
                {
                    records += connection.Execute(
                        """
                        INSERT INTO google_analytics_devices
                        (domain, property_id, date, device_category, sessions, active_users, fetched_at)
                        VALUES (@domain, @propertyId, @date, @deviceCategory, @sessions, @activeUsers, @fetchedAt)
                        """,
                        new
                        {
                            domain,
                            propertyId,
                            date = row.Date,
                            deviceCategory = row.DeviceCategory,
                            sessions = row.Sessions,
                            activeUsers = row.ActiveUsers,
                            fetchedAt,
                        },
                        transaction);
                }

        }).ConfigureAwait(false);
        return records;
    }

    public Task<List<GoogleAnalyticsDailyRow>> GetDailyAsync(string domainOrAll, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var filter = DomainFilter(domainOrAll);
            var rows = connection.Query<GoogleAnalyticsDailyRow>(
                $"""
                SELECT date AS Date,
                       COALESCE(SUM(active_users), 0) AS ActiveUsers,
                       COALESCE(SUM(sessions), 0) AS Sessions,
                       COALESCE(SUM(page_views), 0) AS PageViews,
                       COALESCE(SUM(engaged_sessions), 0) AS EngagedSessions,
                       COALESCE(SUM(event_count), 0) AS EventCount,
                       CASE WHEN SUM(sessions) > 0
                            THEN SUM(engaged_sessions) * 1.0 / SUM(sessions)
                            ELSE AVG(engagement_rate)
                       END AS EngagementRate
                FROM google_analytics_daily
                WHERE date >= @start AND date <= @end
                  {filter.WhereClause}
                GROUP BY date
                ORDER BY date
                """,
                WithRange(filter.Parameters, start, end)).AsList();
            return rows;
        });
    }

    public Task<List<GoogleAnalyticsPageRow>> GetPagesAsync(string domainOrAll, string start, string end, int limit)
    {
        return _db.ReadAsync(connection =>
        {
            var filter = DomainFilter(domainOrAll);
            return connection.Query<GoogleAnalyticsPageRow>(
                $"""
                SELECT page_path AS PagePath,
                       COALESCE(SUM(active_users), 0) AS ActiveUsers,
                       COALESCE(SUM(sessions), 0) AS Sessions,
                       COALESCE(SUM(page_views), 0) AS PageViews,
                       COALESCE(SUM(engaged_sessions), 0) AS EngagedSessions,
                       CASE WHEN SUM(sessions) > 0
                            THEN SUM(engaged_sessions) * 1.0 / SUM(sessions)
                            ELSE AVG(engagement_rate)
                       END AS EngagementRate
                FROM google_analytics_pages
                WHERE period_start = @start AND period_end = @end
                  {filter.WhereClause}
                GROUP BY page_path
                ORDER BY PageViews DESC
                LIMIT @limit
                """,
                WithRange(filter.Parameters, start, end, limit)).AsList();
        });
    }

    public Task<List<GoogleAnalyticsSourceRow>> GetSourcesAsync(string domainOrAll, string start, string end, int limit)
    {
        return _db.ReadAsync(connection =>
        {
            var filter = DomainFilter(domainOrAll);
            return connection.Query<GoogleAnalyticsSourceRow>(
                $"""
                SELECT source_medium AS SourceMedium,
                       COALESCE(SUM(sessions), 0) AS Sessions,
                       COALESCE(SUM(active_users), 0) AS ActiveUsers,
                       COALESCE(SUM(key_events), 0) AS KeyEvents
                FROM google_analytics_sources
                WHERE period_start = @start AND period_end = @end
                  {filter.WhereClause}
                GROUP BY source_medium
                ORDER BY Sessions DESC
                LIMIT @limit
                """,
                WithRange(filter.Parameters, start, end, limit)).AsList();
        });
    }

    public Task<List<GoogleAnalyticsEventRow>> GetEventsAsync(string domainOrAll, string start, string end, int limit)
    {
        return _db.ReadAsync(connection =>
        {
            var filter = DomainFilter(domainOrAll);
            return connection.Query<GoogleAnalyticsEventRow>(
                $"""
                SELECT event_name AS EventName,
                       COALESCE(SUM(event_count), 0) AS EventCount,
                       COALESCE(SUM(key_events), 0) AS KeyEvents
                FROM google_analytics_events
                WHERE period_start = @start AND period_end = @end
                  {filter.WhereClause}
                GROUP BY event_name
                ORDER BY EventCount DESC
                LIMIT @limit
                """,
                WithRange(filter.Parameters, start, end, limit)).AsList();
        });
    }

    public Task<List<GoogleAnalyticsDeviceRow>> GetDevicesAsync(string domainOrAll, string start, string end)
    {
        return _db.ReadAsync(connection =>
        {
            var filter = DomainFilter(domainOrAll);
            return connection.Query<GoogleAnalyticsDeviceRow>(
                $"""
                SELECT date AS Date,
                       device_category AS DeviceCategory,
                       COALESCE(SUM(sessions), 0) AS Sessions,
                       COALESCE(SUM(active_users), 0) AS ActiveUsers
                FROM google_analytics_devices
                WHERE date >= @start AND date <= @end
                  {filter.WhereClause}
                GROUP BY date, device_category
                ORDER BY date, device_category
                """,
                WithRange(filter.Parameters, start, end)).AsList();
        });
    }

    private static (string WhereClause, DynamicParameters Parameters) DomainFilter(string domainOrAll)
    {
        var parameters = new DynamicParameters();
        if (domainOrAll == "all")
        {
            return ("", parameters);
        }

        parameters.Add("domain", SiteIdentity.NormalizeDomain(domainOrAll));
        return ("AND domain = @domain", parameters);
    }

    private static DynamicParameters WithRange(DynamicParameters parameters, string start, string end, int? limit = null)
    {
        parameters.Add("start", start);
        parameters.Add("end", end);
        if (limit.HasValue)
        {
            parameters.Add("limit", limit.Value);
        }
        return parameters;
    }
}

public sealed class GoogleAnalyticsRollup
{
    public List<GoogleAnalyticsDailyRow> Daily { get; set; } = new();
    public List<GoogleAnalyticsPageRow> Pages { get; set; } = new();
    public List<GoogleAnalyticsSourceRow> Sources { get; set; } = new();
    public List<GoogleAnalyticsEventRow> Events { get; set; } = new();
    public List<GoogleAnalyticsDeviceRow> Devices { get; set; } = new();
}
