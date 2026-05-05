using Microsoft.Data.Sqlite;

namespace Numeris.Services.Database;

internal static class Migrations
{
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
            status TEXT NOT NULL DEFAULT 'mock',
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
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('cf', 'cloudflare', 'mock');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('sc', 'search_console', 'mock');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('ps', 'play_store', 'mock');
        INSERT OR IGNORE INTO connections (id, source, status) VALUES ('wa', 'web_analytics', 'mock');
        INSERT OR IGNORE INTO meta (key, value) VALUES ('schema_version', '1');
        """;

    public static void RunAll(SqliteConnection connection)
    {
        ExecuteBatch(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        ExecuteBatch(connection, SchemaSql);
        ExecuteBatch(connection, DefaultConnectionsSql);
    }

    private static void ExecuteBatch(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
