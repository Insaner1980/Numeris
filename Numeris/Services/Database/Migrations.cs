using Dapper;
using Microsoft.Data.Sqlite;

namespace Numeris.Services.Database;

internal static class Migrations
{
    private const int CurrentSchemaVersion = 4;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS cloudflare_traffic (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            pageviews INTEGER NOT NULL DEFAULT 0,
            unique_visitors INTEGER NOT NULL DEFAULT 0,
            requests INTEGER NOT NULL DEFAULT 0,
            cached_requests INTEGER NOT NULL DEFAULT 0,
            cached_bytes INTEGER NOT NULL DEFAULT 0,
            total_bytes INTEGER NOT NULL DEFAULT 0,
            threats INTEGER NOT NULL DEFAULT 0,
            top_country TEXT,
            top_path TEXT,
            fetched_at TEXT NOT NULL,
            UNIQUE(domain, date)
        );

        CREATE TABLE IF NOT EXISTS cloudflare_countries (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            country TEXT NOT NULL,
            visitors INTEGER NOT NULL DEFAULT 0,
            UNIQUE(domain, date, country)
        );

        CREATE TABLE IF NOT EXISTS cloudflare_status_codes (
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            status_code INTEGER NOT NULL,
            requests INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY (domain, date, status_code)
        );

        CREATE INDEX IF NOT EXISTS idx_cf_status_domain_date
            ON cloudflare_status_codes(domain, date);

        CREATE TABLE IF NOT EXISTS search_console (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            site_url TEXT NOT NULL,
            date TEXT NOT NULL,
            kind TEXT NOT NULL DEFAULT 'query',
            query TEXT NOT NULL,
            page TEXT,
            clicks INTEGER NOT NULL DEFAULT 0,
            impressions INTEGER NOT NULL DEFAULT 0,
            ctr REAL NOT NULL DEFAULT 0.0,
            position REAL NOT NULL DEFAULT 0.0,
            country TEXT,
            fetched_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_sc_site_date ON search_console(site_url, date);
        CREATE INDEX IF NOT EXISTS idx_sc_query ON search_console(query);
        CREATE INDEX IF NOT EXISTS idx_sc_kind ON search_console(kind);

        CREATE TABLE IF NOT EXISTS search_devices (
            site_url TEXT NOT NULL,
            date TEXT NOT NULL,
            device TEXT NOT NULL,
            clicks INTEGER NOT NULL DEFAULT 0,
            impressions INTEGER NOT NULL DEFAULT 0,
            ctr REAL NOT NULL DEFAULT 0,
            position REAL NOT NULL DEFAULT 0,
            PRIMARY KEY (site_url, date, device)
        );

        CREATE INDEX IF NOT EXISTS idx_search_devices_site_date
            ON search_devices(site_url, date);

        CREATE TABLE IF NOT EXISTS search_page_queries (
            site_url TEXT NOT NULL,
            period_start TEXT NOT NULL,
            period_end TEXT NOT NULL,
            page TEXT NOT NULL,
            query TEXT NOT NULL,
            clicks INTEGER NOT NULL DEFAULT 0,
            impressions INTEGER NOT NULL DEFAULT 0,
            ctr REAL NOT NULL DEFAULT 0,
            position REAL NOT NULL DEFAULT 0,
            PRIMARY KEY (site_url, period_start, period_end, page, query)
        );

        CREATE INDEX IF NOT EXISTS idx_search_page_queries_site_page
            ON search_page_queries(site_url, page);

        CREATE TABLE IF NOT EXISTS sitemap_urls (
            domain TEXT NOT NULL,
            url TEXT NOT NULL,
            discovered_at TEXT NOT NULL,
            last_seen_at TEXT NOT NULL,
            removed_at TEXT,
            last_inspected_at TEXT,
            verdict TEXT,
            coverage_state TEXT,
            indexing_state TEXT,
            robots_txt_state TEXT,
            page_fetch_state TEXT,
            crawled_as TEXT,
            last_crawl_time TEXT,
            PRIMARY KEY (domain, url)
        );

        CREATE INDEX IF NOT EXISTS idx_sitemap_urls_domain
            ON sitemap_urls(domain);
        CREATE INDEX IF NOT EXISTS idx_sitemap_urls_inspect_age
            ON sitemap_urls(domain, last_inspected_at);

        CREATE TABLE IF NOT EXISTS play_installs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            package_name TEXT NOT NULL,
            date TEXT NOT NULL,
            installs INTEGER NOT NULL DEFAULT 0,
            uninstalls INTEGER NOT NULL DEFAULT 0,
            updates INTEGER NOT NULL DEFAULT 0,
            active_installs INTEGER NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            UNIQUE(package_name, date)
        );

        CREATE TABLE IF NOT EXISTS play_ratings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            package_name TEXT NOT NULL,
            date TEXT NOT NULL,
            total_ratings INTEGER NOT NULL DEFAULT 0,
            average_rating REAL NOT NULL DEFAULT 0.0,
            star_1 INTEGER NOT NULL DEFAULT 0,
            star_2 INTEGER NOT NULL DEFAULT 0,
            star_3 INTEGER NOT NULL DEFAULT 0,
            star_4 INTEGER NOT NULL DEFAULT 0,
            star_5 INTEGER NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            UNIQUE(package_name, date)
        );

        CREATE TABLE IF NOT EXISTS play_revenue (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            package_name TEXT NOT NULL,
            date TEXT NOT NULL,
            purchases INTEGER NOT NULL DEFAULT 0,
            revenue REAL NOT NULL DEFAULT 0.0,
            currency TEXT NOT NULL DEFAULT 'EUR',
            fetched_at TEXT NOT NULL,
            UNIQUE(package_name, date)
        );

        CREATE TABLE IF NOT EXISTS play_crashes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            package_name TEXT NOT NULL,
            date TEXT NOT NULL,
            crash_rate REAL NOT NULL DEFAULT 0.0,
            anr_rate REAL NOT NULL DEFAULT 0.0,
            fetched_at TEXT NOT NULL,
            UNIQUE(package_name, date)
        );

        CREATE INDEX IF NOT EXISTS idx_pi_date ON play_installs(package_name, date);
        CREATE INDEX IF NOT EXISTS idx_pr_date ON play_revenue(package_name, date);

        CREATE TABLE IF NOT EXISTS connections (
            id TEXT PRIMARY KEY,
            source TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'disconnected',
            config TEXT,
            last_sync TEXT
        );

        CREATE TABLE IF NOT EXISTS web_analytics_sites (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL UNIQUE,
            site_tag TEXT NOT NULL,
            discovered_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS web_analytics_daily (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            visits INTEGER NOT NULL DEFAULT 0,
            page_views INTEGER NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            UNIQUE(domain, date)
        );

        CREATE TABLE IF NOT EXISTS web_analytics_referrers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            referrer TEXT NOT NULL,
            visits INTEGER NOT NULL DEFAULT 0,
            UNIQUE(domain, date, referrer)
        );

        CREATE TABLE IF NOT EXISTS web_analytics_pages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            path TEXT NOT NULL,
            page_views INTEGER NOT NULL DEFAULT 0,
            UNIQUE(domain, date, path)
        );

        CREATE TABLE IF NOT EXISTS web_analytics_countries (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            country TEXT NOT NULL,
            visits INTEGER NOT NULL DEFAULT 0,
            UNIQUE(domain, date, country)
        );

        CREATE INDEX IF NOT EXISTS idx_wa_daily ON web_analytics_daily(domain, date);
        CREATE INDEX IF NOT EXISTS idx_wa_referrers ON web_analytics_referrers(domain, date);
        CREATE INDEX IF NOT EXISTS idx_wa_pages ON web_analytics_pages(domain, date);
        CREATE INDEX IF NOT EXISTS idx_wa_countries ON web_analytics_countries(domain, date);

        CREATE TABLE IF NOT EXISTS performance_urls (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            url TEXT NOT NULL UNIQUE,
            origin TEXT NOT NULL,
            source TEXT NOT NULL DEFAULT 'manual',
            enabled INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_perf_urls_origin
            ON performance_urls(origin);

        CREATE TABLE IF NOT EXISTS crux_metric_points (
            target_type TEXT NOT NULL,
            target TEXT NOT NULL,
            form_factor TEXT NOT NULL,
            collection_start TEXT NOT NULL,
            collection_end TEXT NOT NULL,
            metric TEXT NOT NULL,
            p75 REAL,
            good_density REAL,
            needs_improvement_density REAL,
            poor_density REAL,
            raw_json TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (target_type, target, form_factor, collection_end, metric)
        );

        CREATE INDEX IF NOT EXISTS idx_crux_metric_target
            ON crux_metric_points(target_type, target, form_factor, metric, collection_end);

        CREATE TABLE IF NOT EXISTS pagespeed_runs (
            url TEXT NOT NULL,
            strategy TEXT NOT NULL,
            analysis_utc TEXT NOT NULL,
            final_url TEXT,
            performance_score REAL,
            accessibility_score REAL,
            best_practices_score REAL,
            seo_score REAL,
            lighthouse_version TEXT,
            runtime_error TEXT,
            warnings_json TEXT,
            raw_json TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (url, strategy, analysis_utc)
        );

        CREATE TABLE IF NOT EXISTS pagespeed_audits (
            url TEXT NOT NULL,
            strategy TEXT NOT NULL,
            analysis_utc TEXT NOT NULL,
            audit_id TEXT NOT NULL,
            title TEXT,
            score REAL,
            numeric_value REAL,
            numeric_unit TEXT,
            display_value TEXT,
            score_display_mode TEXT,
            details_json TEXT,
            PRIMARY KEY (url, strategy, analysis_utc, audit_id)
        );

        CREATE INDEX IF NOT EXISTS idx_pagespeed_runs_url
            ON pagespeed_runs(url, strategy, analysis_utc);

        CREATE TABLE IF NOT EXISTS bing_sites (
            site_url TEXT PRIMARY KEY,
            source TEXT NOT NULL DEFAULT 'manual',
            enabled INTEGER NOT NULL DEFAULT 1,
            discovered_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS bing_rank_traffic (
            site_url TEXT NOT NULL,
            date TEXT NOT NULL,
            clicks INTEGER,
            impressions INTEGER,
            raw_json TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (site_url, date)
        );

        CREATE TABLE IF NOT EXISTS bing_query_stats (
            site_url TEXT NOT NULL,
            query TEXT NOT NULL,
            date TEXT NOT NULL DEFAULT '',
            clicks INTEGER,
            impressions INTEGER,
            avg_click_position REAL,
            avg_impression_position REAL,
            raw_json TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (site_url, query, date)
        );

        CREATE TABLE IF NOT EXISTS bing_page_stats (
            site_url TEXT NOT NULL,
            page_url TEXT NOT NULL,
            date TEXT NOT NULL DEFAULT '',
            clicks INTEGER,
            impressions INTEGER,
            raw_json TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (site_url, page_url, date)
        );

        CREATE TABLE IF NOT EXISTS bing_raw_items (
            method TEXT NOT NULL,
            site_url TEXT NOT NULL DEFAULT '',
            item_key TEXT NOT NULL,
            raw_json TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (method, site_url, item_key)
        );

        CREATE INDEX IF NOT EXISTS idx_bing_raw_method
            ON bing_raw_items(method, site_url);

        CREATE TABLE IF NOT EXISTS youtube_channels (
            channel_id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            uploads_playlist_id TEXT NOT NULL,
            view_count INTEGER NOT NULL DEFAULT 0,
            subscriber_count INTEGER NOT NULL DEFAULT 0,
            video_count INTEGER NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS youtube_videos (
            video_id TEXT PRIMARY KEY,
            channel_id TEXT NOT NULL,
            title TEXT NOT NULL,
            published_at TEXT NOT NULL,
            duration TEXT,
            thumbnail_url TEXT,
            view_count INTEGER NOT NULL DEFAULT 0,
            like_count INTEGER NOT NULL DEFAULT 0,
            comment_count INTEGER NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_youtube_videos_channel_published
            ON youtube_videos(channel_id, published_at);

        CREATE TABLE IF NOT EXISTS youtube_daily (
            channel_id TEXT NOT NULL,
            date TEXT NOT NULL,
            views INTEGER NOT NULL DEFAULT 0,
            estimated_minutes_watched REAL NOT NULL DEFAULT 0,
            average_view_duration REAL NOT NULL DEFAULT 0,
            subscribers_gained INTEGER NOT NULL DEFAULT 0,
            subscribers_lost INTEGER NOT NULL DEFAULT 0,
            likes INTEGER NOT NULL DEFAULT 0,
            comments INTEGER NOT NULL DEFAULT 0,
            shares INTEGER NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (channel_id, date)
        );

        CREATE TABLE IF NOT EXISTS youtube_video_stats (
            channel_id TEXT NOT NULL,
            video_id TEXT NOT NULL,
            period_start TEXT NOT NULL,
            period_end TEXT NOT NULL,
            views INTEGER NOT NULL DEFAULT 0,
            estimated_minutes_watched REAL NOT NULL DEFAULT 0,
            average_view_duration REAL NOT NULL DEFAULT 0,
            average_view_percentage REAL NOT NULL DEFAULT 0,
            likes INTEGER NOT NULL DEFAULT 0,
            comments INTEGER NOT NULL DEFAULT 0,
            shares INTEGER NOT NULL DEFAULT 0,
            subscribers_gained INTEGER NOT NULL DEFAULT 0,
            subscribers_lost INTEGER NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (channel_id, video_id, period_start, period_end)
        );

        CREATE TABLE IF NOT EXISTS youtube_countries (
            channel_id TEXT NOT NULL,
            period_start TEXT NOT NULL,
            period_end TEXT NOT NULL,
            country TEXT NOT NULL,
            views INTEGER NOT NULL DEFAULT 0,
            estimated_minutes_watched REAL NOT NULL DEFAULT 0,
            average_view_duration REAL NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (channel_id, period_start, period_end, country)
        );

        CREATE TABLE IF NOT EXISTS youtube_traffic_sources (
            channel_id TEXT NOT NULL,
            period_start TEXT NOT NULL,
            period_end TEXT NOT NULL,
            source_type TEXT NOT NULL,
            views INTEGER NOT NULL DEFAULT 0,
            estimated_minutes_watched REAL NOT NULL DEFAULT 0,
            average_view_duration REAL NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (channel_id, period_start, period_end, source_type)
        );

        CREATE TABLE IF NOT EXISTS youtube_devices (
            channel_id TEXT NOT NULL,
            period_start TEXT NOT NULL,
            period_end TEXT NOT NULL,
            device_type TEXT NOT NULL,
            views INTEGER NOT NULL DEFAULT 0,
            estimated_minutes_watched REAL NOT NULL DEFAULT 0,
            average_view_duration REAL NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (channel_id, period_start, period_end, device_type)
        );

        CREATE TABLE IF NOT EXISTS youtube_retention_points (
            channel_id TEXT NOT NULL,
            video_id TEXT NOT NULL,
            period_start TEXT NOT NULL,
            period_end TEXT NOT NULL,
            elapsed_ratio REAL NOT NULL DEFAULT 0,
            audience_watch_ratio REAL NOT NULL DEFAULT 0,
            relative_retention_performance REAL NOT NULL DEFAULT 0,
            fetched_at TEXT NOT NULL,
            PRIMARY KEY (channel_id, video_id, period_start, period_end, elapsed_ratio)
        );

        CREATE TABLE IF NOT EXISTS uptime_checks (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            checked_at TEXT NOT NULL,
            status TEXT NOT NULL,
            status_code INTEGER,
            response_ms INTEGER,
            error_message TEXT,
            UNIQUE(domain, checked_at)
        );

        CREATE INDEX IF NOT EXISTS idx_uptime_domain_checked
            ON uptime_checks(domain, checked_at);

        CREATE TABLE IF NOT EXISTS meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;

    private const string DefaultConnectionsSql = """
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('cf', 'cloudflare', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('sc', 'search_console', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('ps', 'play_store', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('wa', 'web_analytics', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('perf', 'performance', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('crux', 'crux', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('pagespeed', 'pagespeed', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('bing', 'bing_webmaster', 'disconnected');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('youtube', 'youtube', 'disconnected');
        INSERT OR IGNORE INTO performance_urls (url, origin, source, enabled, created_at)
            VALUES ('https://finnvek.com/', 'https://finnvek.com', 'home', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO performance_urls (url, origin, source, enabled, created_at)
            VALUES ('https://knittoolsapp.com/', 'https://knittoolsapp.com', 'home', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO bing_sites (site_url, source, enabled, discovered_at)
            VALUES ('https://finnvek.com/', 'manual', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO bing_sites (site_url, source, enabled, discovered_at)
            VALUES ('https://knittoolsapp.com/', 'manual', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO meta (key, value) VALUES ('schema_version', '4');
        """;

    public static void RunAll(SqliteConnection connection)
    {
        ExecuteBatch(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        ExecuteBatch(connection, SchemaSql);
        RunPendingMigrations(connection);
        ExecuteBatch(connection, DefaultConnectionsSql);
    }

    private static void RunPendingMigrations(SqliteConnection connection)
    {
        var version = GetUserVersion(connection);
        if (version == 0)
        {
            version = GetMetaSchemaVersion(connection);
        }

        if (version < 2)
        {
            RunV2Migration(connection);
        }

        if (version < 3)
        {
            RunV3Migration(connection);
        }

        if (version < 4)
        {
            RunV4Migration(connection);
        }

        SetSchemaVersion(connection, CurrentSchemaVersion);
    }

    private static void RunV2Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            if (TableExists(connection, transaction, "bing_page_stats")
                && !ColumnExists(connection, transaction, "bing_page_stats", "date"))
            {
                ExecuteBatch(
                    connection,
                    """
                    ALTER TABLE bing_page_stats RENAME TO bing_page_stats_old;

                    CREATE TABLE bing_page_stats (
                        site_url TEXT NOT NULL,
                        page_url TEXT NOT NULL,
                        date TEXT NOT NULL DEFAULT '',
                        clicks INTEGER,
                        impressions INTEGER,
                        raw_json TEXT NOT NULL,
                        fetched_at TEXT NOT NULL,
                        PRIMARY KEY (site_url, page_url, date)
                    );

                    INSERT OR REPLACE INTO bing_page_stats
                    (site_url, page_url, date, clicks, impressions, raw_json, fetched_at)
                    SELECT site_url, page_url, '', clicks, impressions, raw_json, fetched_at
                    FROM bing_page_stats_old;

                    DROP TABLE bing_page_stats_old;
                    """,
                    transaction);
            }

            SetSchemaVersion(connection, CurrentSchemaVersion, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void RunV3Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            ExecuteBatch(
                connection,
                """
                DELETE FROM cloudflare_traffic WHERE top_path IS NOT NULL OR top_country IS NOT NULL;
                DELETE FROM cloudflare_countries;
                DELETE FROM search_console
                    WHERE query IN (
                        'knitting counter app', 'neulonta sovellus', 'knitting row counter',
                        'knitting calculator', 'gauge converter knitting', 'yarn estimator',
                        'knittools', 'knittools app', 'finnvek', 'knitting app android',
                        'best knitting apps 2026', 'neulonta laskuri', 'puikkojen koot',
                        'lankatarve laskuri', 'neuleohje laskuri', 'knitting pattern calculator',
                        'stitch counter', 'yarn weight calculator', 'knitting gauge',
                        'crochet counter app', 'finnvek apps', 'finnvek knittools',
                        'finnish app developer', 'finnvek software'
                    );
                DELETE FROM search_devices;
                DELETE FROM search_page_queries;
                DELETE FROM sitemap_urls
                    WHERE url LIKE 'https://finnvek.com/features/tool-%'
                       OR url LIKE 'https://finnvek.com/guide/lesson-%'
                       OR url LIKE 'https://finnvek.com/blog/post-%'
                       OR url LIKE 'https://finnvek.com/support/topic-%'
                       OR url LIKE 'https://knittoolsapp.com/features/tool-%'
                       OR url LIKE 'https://knittoolsapp.com/guide/lesson-%'
                       OR url LIKE 'https://knittoolsapp.com/blog/post-%'
                       OR url LIKE 'https://knittoolsapp.com/support/topic-%';
                DELETE FROM web_analytics_daily;
                DELETE FROM web_analytics_referrers;
                DELETE FROM web_analytics_pages;
                DELETE FROM web_analytics_countries;
                DELETE FROM play_installs;
                DELETE FROM play_ratings;
                DELETE FROM play_revenue;
                DELETE FROM play_crashes;
                DELETE FROM uptime_checks;
                UPDATE connections
                SET status = 'disconnected', last_sync = NULL
                WHERE status = 'mock';
                """,
                transaction);

            SetSchemaVersion(connection, CurrentSchemaVersion, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void RunV4Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            ExecuteBatch(
                connection,
                """
                INSERT OR IGNORE INTO connections (id, source, status)
                VALUES ('youtube', 'youtube', 'disconnected');
                """,
                transaction);

            SetSchemaVersion(connection, CurrentSchemaVersion, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return int.TryParse(cmd.ExecuteScalar()?.ToString(), out var version) ? version : 0;
    }

    private static int GetMetaSchemaVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        return int.TryParse(cmd.ExecuteScalar()?.ToString(), out var version) ? version : 0;
    }

    private static void SetSchemaVersion(SqliteConnection connection, int version, SqliteTransaction? transaction = null)
    {
        ExecuteBatch(connection, $"PRAGMA user_version = {version};", transaction);
        ExecuteBatch(
            connection,
            """
            INSERT INTO meta (key, value) VALUES ('schema_version', @version)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            transaction,
            new SqliteParameter("@version", version.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static bool TableExists(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @table";
        cmd.Parameters.AddWithValue("@table", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool ColumnExists(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT 1 FROM pragma_table_info(@table) WHERE name = @column";
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return cmd.ExecuteScalar() is not null;
    }

    private static void ExecuteBatch(
        SqliteConnection connection,
        string batchSql,
        SqliteTransaction? transaction = null,
        params SqliteParameter[] parameters)
    {
        var dynamicParameters = new DynamicParameters();
        foreach (var parameter in parameters)
        {
            dynamicParameters.Add(parameter.ParameterName.TrimStart('@', '$', ':'), parameter.Value);
        }
        connection.Execute(batchSql, dynamicParameters, transaction);
    }
}
