using System;
using Dapper;
using Microsoft.Data.Sqlite;
using Numeris.Services.Api;

namespace Numeris.Services.Database;

internal static class Migrations
{
    private const int CurrentSchemaVersion = 11;

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

        CREATE INDEX IF NOT EXISTS idx_cf_countries_domain_date
            ON cloudflare_countries(domain, date);

        CREATE TABLE IF NOT EXISTS cloudflare_pages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            domain TEXT NOT NULL,
            date TEXT NOT NULL,
            path TEXT NOT NULL,
            requests INTEGER NOT NULL DEFAULT 0,
            UNIQUE(domain, date, path)
        );

        CREATE INDEX IF NOT EXISTS idx_cf_pages_domain_date
            ON cloudflare_pages(domain, date);

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
        INSERT OR IGNORE INTO performance_urls (url, origin, source, enabled, created_at)
            VALUES ('https://finnvek.com/', 'https://finnvek.com', 'home', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO performance_urls (url, origin, source, enabled, created_at)
            VALUES ('https://knittoolsapp.com/', 'https://knittoolsapp.com', 'home', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO bing_sites (site_url, source, enabled, discovered_at)
            VALUES ('https://finnvek.com/', 'manual', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO bing_sites (site_url, source, enabled, discovered_at)
            VALUES ('https://knittoolsapp.com/', 'manual', 1, strftime('%Y-%m-%dT%H:%M:%S', 'now'));
        INSERT OR IGNORE INTO meta (key, value) VALUES ('schema_version', '11');
        """;

    public static void RunAll(SqliteConnection connection)
    {
        var version = GetUserVersion(connection);
        if (version < 0) throw new InvalidOperationException("The database schema version is invalid.");
        if (version == 0) version = GetMetaSchemaVersion(connection);
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException("The database schema is newer than this application supports.");
        }
        ExecuteBatch(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        ExecuteBatch(connection, SchemaSql);
        RunPendingMigrations(connection, version);
        ExecuteBatch(connection, DefaultConnectionsSql);
    }

    private static void RunPendingMigrations(SqliteConnection connection, int version)
    {
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

        if (version < 5)
        {
            RunV5Migration(connection);
        }

        if (version < 6)
        {
            RunV6Migration(connection);
        }

        if (version < 8)
        {
            RunV8Migration(connection);
        }

        if (version < 9)
        {
            RunV9Migration(connection);
        }

        if (version < 11)
        {
            RunV11Migration(connection);
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

            SetSchemaVersion(connection, 2, transaction);
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

            SetSchemaVersion(connection, 3, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void RunV4Migration(SqliteConnection connection)
        => SetSchemaVersion(connection, 4);

    private static void RunV5Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            ExecuteBatch(
                connection,
                """
                CREATE TABLE IF NOT EXISTS cloudflare_pages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    domain TEXT NOT NULL,
                    date TEXT NOT NULL,
                    path TEXT NOT NULL,
                    requests INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(domain, date, path)
                );

                CREATE INDEX IF NOT EXISTS idx_cf_countries_domain_date
                    ON cloudflare_countries(domain, date);

                CREATE INDEX IF NOT EXISTS idx_cf_pages_domain_date
                    ON cloudflare_pages(domain, date);
                """,
                transaction);

            SetSchemaVersion(connection, 5, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void RunV6Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            ExecuteBatch(
                connection,
                """
                DELETE FROM connections WHERE id = 'youtube' OR source = 'youtube';
                DROP TABLE IF EXISTS youtube_retention_points;
                DROP TABLE IF EXISTS youtube_devices;
                DROP TABLE IF EXISTS youtube_traffic_sources;
                DROP TABLE IF EXISTS youtube_countries;
                DROP TABLE IF EXISTS youtube_video_stats;
                DROP TABLE IF EXISTS youtube_daily;
                DROP TABLE IF EXISTS youtube_videos;
                DROP TABLE IF EXISTS youtube_channels;
                """,
                transaction);

            SetSchemaVersion(connection, 6, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void RunV8Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var (table, keys) in new[]
        {
            ("bing_rank_traffic", "saved.site_url = old.site_url"),
            ("bing_query_stats", "saved.site_url = old.site_url AND saved.query = old.query"),
            ("bing_page_stats", "saved.site_url = old.site_url AND saved.page_url = old.page_url"),
        })
        {
            var rows = connection.Query<(long Id, string Date)>(
                $"SELECT rowid, date FROM {table} WHERE date LIKE '/Date(%' ORDER BY fetched_at", transaction: transaction).AsList();
            foreach (var row in rows)
            {
                string date;
                try { date = BingWebmasterClient.NormalizeDate(row.Date); }
                catch (FormatException) { continue; }
                connection.Execute($"""
                    DELETE FROM {table} AS old
                    WHERE old.rowid = @id AND EXISTS (
                        SELECT 1 FROM {table} AS saved
                        WHERE saved.date = @date AND {keys} AND saved.fetched_at >= old.fetched_at
                    );
                    UPDATE OR REPLACE {table} SET date = @date WHERE rowid = @id;
                    """, new { id = row.Id, date }, transaction);
            }
        }
        SetSchemaVersion(connection, 8, transaction);
        transaction.Commit();
    }

    private static void RunV9Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        // Earlier breakdowns stored overlapping period totals under the fetch date.
        // Their daily distribution is unknown; fetch them again without altering daily totals or connections.
        ExecuteBatch(connection, """
            DELETE FROM cloudflare_countries;
            DELETE FROM cloudflare_pages;
            DELETE FROM web_analytics_referrers;
            DELETE FROM web_analytics_pages;
            DELETE FROM web_analytics_countries;
            """, transaction);
        SetSchemaVersion(connection, 9, transaction);
        transaction.Commit();
    }

    private static void RunV11Migration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        ExecuteBatch(connection, """
            DELETE FROM connections WHERE id = 'ga4' OR source = 'google_analytics';
            DROP TABLE IF EXISTS google_analytics_daily;
            DROP TABLE IF EXISTS google_analytics_active_users;
            DROP TABLE IF EXISTS google_analytics_pages;
            DROP TABLE IF EXISTS google_analytics_sources;
            DROP TABLE IF EXISTS google_analytics_events;
            DROP TABLE IF EXISTS google_analytics_devices;
            """, transaction);
        SetSchemaVersion(connection, 11, transaction);
        transaction.Commit();
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return int.TryParse(cmd.ExecuteScalar()?.ToString(), out var version) ? version : 0;
    }

    private static int GetMetaSchemaVersion(SqliteConnection connection)
    {
        if (connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta'") == 0) return 0;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        var value = cmd.ExecuteScalar();
        if (value is null) return 0;
        if (!int.TryParse(value.ToString(), out var version) || version < 0)
        {
            throw new InvalidOperationException("The database schema version is invalid.");
        }
        return version;
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
