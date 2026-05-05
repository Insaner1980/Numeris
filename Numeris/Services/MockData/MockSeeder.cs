using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Database;

namespace Numeris.Services.MockData;

public sealed class MockSeeder
{
    private readonly SqliteDatabase _db;
    private readonly Random _rng = new();

    public MockSeeder(SqliteDatabase db)
    {
        _db = db;
    }

    public Task<SeedResult> SeedAsync()
    {
        return _db.WriteAsync(connection =>
        {
            var result = new SeedResult { DaysGenerated = 90 };
            ClearAll(connection);
            result.RecordsInserted += SeedCloudflareAndSitemap(connection);
            result.RecordsInserted += SeedSearchConsole(connection);
            result.RecordsInserted += SeedPlayStore(connection);
            UpdateConnectionSyncTimes(connection);
            return result;
        });
    }

    private static void ClearAll(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM cloudflare_traffic;
            DELETE FROM cloudflare_countries;
            DELETE FROM cloudflare_status_codes;
            DELETE FROM search_console;
            DELETE FROM search_devices;
            DELETE FROM search_page_queries;
            DELETE FROM sitemap_urls;
            DELETE FROM web_analytics_daily;
            DELETE FROM web_analytics_referrers;
            DELETE FROM web_analytics_pages;
            DELETE FROM web_analytics_countries;
            DELETE FROM web_analytics_sites;
            DELETE FROM uptime_checks;
            DELETE FROM play_installs;
            DELETE FROM play_ratings;
            DELETE FROM play_revenue;
            DELETE FROM play_crashes;
            """;
        cmd.ExecuteNonQuery();
    }

    private long SeedCloudflareAndSitemap(SqliteConnection connection)
    {
        long records = 0;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        const int days = 90;

        var cfCountries = new (string Country, int Pct)[]
        {
            ("Finland", 40),
            ("Germany", 15),
            ("Sweden", 10),
            ("Norway", 8),
            ("United States", 7),
            ("United Kingdom", 5),
            ("Denmark", 4),
            ("Netherlands", 3),
            ("France", 3),
            ("Other", 5),
        };

        var paths = new[] { "/", "/features", "/guide", "/pricing", "/blog", "/about", "/contact" };
        var pathWeights = new[] { 0.35, 0.20, 0.15, 0.10, 0.10, 0.05, 0.05 };

        var spikeDays = new HashSet<int>();
        for (var i = 0; i < 4; i++)
        {
            spikeDays.Add(_rng.Next(10, 80));
        }

        var domains = new (string Domain, double BaseMin, double BaseMax)[]
        {
            (Domains.KnitTools, 80.0, 200.0),
            (Domains.Finnvek, 20.0, 60.0),
        };

        using var trafficCmd = connection.CreateCommand();
        trafficCmd.CommandText = """
            INSERT OR REPLACE INTO cloudflare_traffic
            (domain, date, pageviews, unique_visitors, requests, cached_requests, cached_bytes,
             total_bytes, threats, top_country, top_path, fetched_at)
            VALUES ($d,$dt,$pv,$uv,$rq,$crq,$cbt,$tbt,$th,$tc,$tp,$fa);
            """;
        AddParam(trafficCmd, "$d"); AddParam(trafficCmd, "$dt");
        AddParam(trafficCmd, "$pv"); AddParam(trafficCmd, "$uv");
        AddParam(trafficCmd, "$rq"); AddParam(trafficCmd, "$crq");
        AddParam(trafficCmd, "$cbt"); AddParam(trafficCmd, "$tbt");
        AddParam(trafficCmd, "$th"); AddParam(trafficCmd, "$tc");
        AddParam(trafficCmd, "$tp"); AddParam(trafficCmd, "$fa");

        using var countryCmd = connection.CreateCommand();
        countryCmd.CommandText = """
            INSERT OR REPLACE INTO cloudflare_countries (domain, date, country, visitors)
            VALUES ($d,$dt,$c,$v);
            """;
        AddParam(countryCmd, "$d"); AddParam(countryCmd, "$dt");
        AddParam(countryCmd, "$c"); AddParam(countryCmd, "$v");

        using var statusCmd = connection.CreateCommand();
        statusCmd.CommandText = """
            INSERT OR REPLACE INTO cloudflare_status_codes (domain, date, status_code, requests)
            VALUES ($d,$dt,$sc,$r);
            """;
        AddParam(statusCmd, "$d"); AddParam(statusCmd, "$dt");
        AddParam(statusCmd, "$sc"); AddParam(statusCmd, "$r");

        using var sitemapCmd = connection.CreateCommand();
        sitemapCmd.CommandText = """
            INSERT OR REPLACE INTO sitemap_urls
            (domain, url, discovered_at, last_seen_at, removed_at)
            VALUES ($d,$u,$ts,$ts,NULL);
            """;
        AddParam(sitemapCmd, "$d"); AddParam(sitemapCmd, "$u"); AddParam(sitemapCmd, "$ts");

        foreach (var (domain, baseMin, baseMax) in domains)
        {
            for (var dayOffset = 0; dayOffset < days; dayOffset++)
            {
                var date = today.AddDays(-(days - 1 - dayOffset));
                var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var dayOfWeek = ((int)date.DayOfWeek + 6) % 7;

                var growth = 1.0 + ((double)dayOffset / days) * 0.15;
                var weekendFactor = dayOfWeek >= 5 ? 0.7 : 1.0;
                var spikeFactor = spikeDays.Contains(dayOffset)
                    ? RangeDouble(1.8, 3.0)
                    : 1.0;

                var baseViews = RangeDouble(baseMin, baseMax);
                var pageviews = (long)(baseViews * growth * weekendFactor * spikeFactor);
                var uniqueVisitors = (long)(pageviews * RangeDouble(0.6, 0.7));
                var requests = pageviews * _rng.Next(3, 6);

                var cacheHitRatio = RangeDouble(0.55, 0.85);
                var cachedRequests = (long)(requests * cacheHitRatio);
                var avgBytes = RangeDouble(12_000.0, 40_000.0);
                var totalBytes = (long)(requests * avgBytes);
                var cachedBytes = (long)(cachedRequests * avgBytes);
                var threats = RangeDouble(0.0, 1.0) > 0.7
                    ? (long)_rng.Next(1, 15)
                    : 0L;

                var topPathIdx = WeightedRandom(pathWeights);
                var topPath = paths[topPathIdx];

                trafficCmd.Parameters[0].Value = domain;
                trafficCmd.Parameters[1].Value = dateStr;
                trafficCmd.Parameters[2].Value = pageviews;
                trafficCmd.Parameters[3].Value = uniqueVisitors;
                trafficCmd.Parameters[4].Value = requests;
                trafficCmd.Parameters[5].Value = cachedRequests;
                trafficCmd.Parameters[6].Value = cachedBytes;
                trafficCmd.Parameters[7].Value = totalBytes;
                trafficCmd.Parameters[8].Value = threats;
                trafficCmd.Parameters[9].Value = "Finland";
                trafficCmd.Parameters[10].Value = topPath;
                trafficCmd.Parameters[11].Value = nowStr;
                trafficCmd.ExecuteNonQuery();
                records++;

                foreach (var (country, pct) in cfCountries)
                {
                    var countryVisitors = (long)(uniqueVisitors * (pct / 100.0) * RangeDouble(0.8, 1.2));
                    if (countryVisitors <= 0) continue;
                    countryCmd.Parameters[0].Value = domain;
                    countryCmd.Parameters[1].Value = dateStr;
                    countryCmd.Parameters[2].Value = country;
                    countryCmd.Parameters[3].Value = countryVisitors;
                    countryCmd.ExecuteNonQuery();
                    records++;
                }

                var s200 = (long)(requests * RangeDouble(0.86, 0.93));
                var s301 = (long)(requests * RangeDouble(0.03, 0.07));
                var s404 = (long)(requests * RangeDouble(0.02, 0.05));
                var s500 = (long)(requests * RangeDouble(0.005, 0.015));
                foreach (var (code, count) in new[] { (200, s200), (301, s301), (404, s404), (500, s500) })
                {
                    if (count <= 0) continue;
                    statusCmd.Parameters[0].Value = domain;
                    statusCmd.Parameters[1].Value = dateStr;
                    statusCmd.Parameters[2].Value = code;
                    statusCmd.Parameters[3].Value = count;
                    statusCmd.ExecuteNonQuery();
                    records++;
                }
            }

            for (var i = 0; i < 50; i++)
            {
                var slug = i switch
                {
                    0 => string.Empty,
                    >= 1 and <= 8 => $"features/tool-{i}",
                    >= 9 and <= 20 => $"guide/lesson-{i - 8}",
                    >= 21 and <= 35 => $"blog/post-{i - 20}",
                    _ => $"support/topic-{i - 35}",
                };
                var url = string.IsNullOrEmpty(slug)
                    ? $"https://{domain}/"
                    : $"https://{domain}/{slug}";
                sitemapCmd.Parameters[0].Value = domain;
                sitemapCmd.Parameters[1].Value = url;
                sitemapCmd.Parameters[2].Value = nowStr;
                sitemapCmd.ExecuteNonQuery();
                records++;
            }
        }

        return records;
    }

    private long SeedSearchConsole(SqliteConnection connection)
    {
        long records = 0;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        const int days = 90;

        var knitQueries = new[]
        {
            "knitting counter app", "neulonta sovellus", "knitting row counter",
            "knitting calculator", "gauge converter knitting", "yarn estimator",
            "knittools", "knittools app", "finnvek", "knitting app android",
            "best knitting apps 2026", "neulonta laskuri", "puikkojen koot",
            "lankatarve laskuri", "neuleohje laskuri", "knitting pattern calculator",
            "stitch counter", "yarn weight calculator", "knitting gauge",
            "crochet counter app",
        };

        var finnvekQueries = new[]
        {
            "finnvek", "finnvek apps", "finnvek knittools",
            "finnish app developer", "finnvek software",
        };

        var siteConfigs = new (string Site, string[] Queries, int MinQ, int MaxQ)[]
        {
            (Domains.KnitTools, knitQueries, 15, 30),
            (Domains.Finnvek, finnvekQueries, 5, 10),
        };

        var scCountries = new[] { "Finland", "Germany", "Sweden", "United States", "United Kingdom", "Norway" };
        var scPages = new[] { "/", "/features", "/guide", "/pricing", "/blog" };

        using var queryCmd = connection.CreateCommand();
        queryCmd.CommandText = """
            INSERT INTO search_console
            (site_url, date, kind, query, page, clicks, impressions, ctr, position, country, fetched_at)
            VALUES ($s,$d,'query',$q,$p,$cl,$im,$ctr,$pos,$c,$f);
            """;
        foreach (var name in new[] { "$s", "$d", "$q", "$p", "$cl", "$im", "$ctr", "$pos", "$c", "$f" })
            AddParam(queryCmd, name);

        using var deviceCmd = connection.CreateCommand();
        deviceCmd.CommandText = """
            INSERT OR REPLACE INTO search_devices
            (site_url, date, device, clicks, impressions, ctr, position)
            VALUES ($s,$d,$dv,$cl,$im,$ctr,$pos);
            """;
        foreach (var name in new[] { "$s", "$d", "$dv", "$cl", "$im", "$ctr", "$pos" })
            AddParam(deviceCmd, name);

        using var pqCmd = connection.CreateCommand();
        pqCmd.CommandText = """
            INSERT OR REPLACE INTO search_page_queries
            (site_url, period_start, period_end, page, query, clicks, impressions, ctr, position)
            VALUES ($s,$ps,$pe,$p,$q,$cl,$im,$ctr,$pos);
            """;
        foreach (var name in new[] { "$s", "$ps", "$pe", "$p", "$q", "$cl", "$im", "$ctr", "$pos" })
            AddParam(pqCmd, name);

        foreach (var (siteUrl, queries, minQ, maxQ) in siteConfigs)
        {
            for (var dayOffset = 0; dayOffset < days; dayOffset++)
            {
                var date = today.AddDays(-(days - 1 - dayOffset));
                var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                var numQueries = _rng.Next(minQ, maxQ);
                for (var i = 0; i < numQueries; i++)
                {
                    var query = queries[_rng.Next(queries.Length)];
                    var isBranded = query.Contains("knittools") || query.Contains("finnvek");

                    var impressions = isBranded ? _rng.Next(20, 200) : _rng.Next(5, 100);
                    var clicks = isBranded
                        ? _rng.Next(2, Math.Min(15, impressions))
                        : _rng.Next(0, Math.Min(10, impressions));
                    var ctr = impressions > 0 ? (double)clicks / impressions : 0.0;
                    var position = isBranded ? RangeDouble(1.0, 5.0) : RangeDouble(3.0, 45.0);
                    var country = scCountries[_rng.Next(scCountries.Length)];
                    var page = scPages[_rng.Next(scPages.Length)];

                    queryCmd.Parameters[0].Value = siteUrl;
                    queryCmd.Parameters[1].Value = dateStr;
                    queryCmd.Parameters[2].Value = query;
                    queryCmd.Parameters[3].Value = page;
                    queryCmd.Parameters[4].Value = clicks;
                    queryCmd.Parameters[5].Value = impressions;
                    queryCmd.Parameters[6].Value = ctr;
                    queryCmd.Parameters[7].Value = position;
                    queryCmd.Parameters[8].Value = country;
                    queryCmd.Parameters[9].Value = nowStr;
                    queryCmd.ExecuteNonQuery();
                    records++;
                }

                var dayClicks = _rng.Next(20, 120);
                var dayImpressions = _rng.Next(200, 900);
                var deviceSplit = new[]
                {
                    ("DESKTOP", RangeDouble(0.55, 0.75)),
                    ("MOBILE", RangeDouble(0.20, 0.40)),
                    ("TABLET", RangeDouble(0.02, 0.08)),
                };
                foreach (var (device, share) in deviceSplit)
                {
                    var dClicks = (long)(dayClicks * share);
                    var dImpressions = (long)(dayImpressions * share);
                    var dCtr = dImpressions > 0 ? (double)dClicks / dImpressions : 0.0;
                    var dPosition = device switch
                    {
                        "DESKTOP" => RangeDouble(4.0, 12.0),
                        "MOBILE" => RangeDouble(6.0, 18.0),
                        _ => RangeDouble(8.0, 25.0),
                    };
                    deviceCmd.Parameters[0].Value = siteUrl;
                    deviceCmd.Parameters[1].Value = dateStr;
                    deviceCmd.Parameters[2].Value = device;
                    deviceCmd.Parameters[3].Value = dClicks;
                    deviceCmd.Parameters[4].Value = dImpressions;
                    deviceCmd.Parameters[5].Value = dCtr;
                    deviceCmd.Parameters[6].Value = dPosition;
                    deviceCmd.ExecuteNonQuery();
                    records++;
                }
            }

            var pqEnd = today;
            var pqStart = today.AddDays(-27);
            var pqStartStr = pqStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var pqEndStr = pqEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var page in scPages)
            {
                var nQueries = _rng.Next(2, 4);
                for (var i = 0; i < nQueries; i++)
                {
                    var query = queries[_rng.Next(queries.Length)];
                    var impressions = _rng.Next(40, 400);
                    var clicks = _rng.Next(0, Math.Min(20, impressions));
                    var ctr = impressions > 0 ? (double)clicks / impressions : 0.0;
                    var position = RangeDouble(2.0, 30.0);

                    pqCmd.Parameters[0].Value = siteUrl;
                    pqCmd.Parameters[1].Value = pqStartStr;
                    pqCmd.Parameters[2].Value = pqEndStr;
                    pqCmd.Parameters[3].Value = page;
                    pqCmd.Parameters[4].Value = query;
                    pqCmd.Parameters[5].Value = clicks;
                    pqCmd.Parameters[6].Value = impressions;
                    pqCmd.Parameters[7].Value = ctr;
                    pqCmd.Parameters[8].Value = position;
                    pqCmd.ExecuteNonQuery();
                    records++;
                }
            }
        }

        return records;
    }

    private long SeedPlayStore(SqliteConnection connection)
    {
        long records = 0;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nowStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        const int days = 90;
        var package = Domains.PlayStorePackage;

        long activeInstalls = 200;
        long totalRatings = 85;

        using var installCmd = connection.CreateCommand();
        installCmd.CommandText = """
            INSERT OR REPLACE INTO play_installs
            (package_name, date, installs, uninstalls, updates, active_installs, fetched_at)
            VALUES ($p,$d,$i,$u,$up,$ai,$f);
            """;
        foreach (var n in new[] { "$p", "$d", "$i", "$u", "$up", "$ai", "$f" }) AddParam(installCmd, n);

        using var revenueCmd = connection.CreateCommand();
        revenueCmd.CommandText = """
            INSERT OR REPLACE INTO play_revenue
            (package_name, date, purchases, revenue, currency, fetched_at)
            VALUES ($p,$d,$pu,$r,'EUR',$f);
            """;
        foreach (var n in new[] { "$p", "$d", "$pu", "$r", "$f" }) AddParam(revenueCmd, n);

        using var ratingCmd = connection.CreateCommand();
        ratingCmd.CommandText = """
            INSERT OR REPLACE INTO play_ratings
            (package_name, date, total_ratings, average_rating, star_1, star_2, star_3, star_4, star_5, fetched_at)
            VALUES ($p,$d,$tr,$ar,$s1,$s2,$s3,$s4,$s5,$f);
            """;
        foreach (var n in new[] { "$p", "$d", "$tr", "$ar", "$s1", "$s2", "$s3", "$s4", "$s5", "$f" })
            AddParam(ratingCmd, n);

        using var crashCmd = connection.CreateCommand();
        crashCmd.CommandText = """
            INSERT OR REPLACE INTO play_crashes
            (package_name, date, crash_rate, anr_rate, fetched_at)
            VALUES ($p,$d,$cr,$ar,$f);
            """;
        foreach (var n in new[] { "$p", "$d", "$cr", "$ar", "$f" }) AddParam(crashCmd, n);

        for (var dayOffset = 0; dayOffset < days; dayOffset++)
        {
            var date = today.AddDays(-(days - 1 - dayOffset));
            var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var growth = 1.0 + ((double)dayOffset / days) * 0.2;

            var installs = (long)(RangeDouble(3.0, 15.0) * growth);
            var uninstalls = (long)(installs * RangeDouble(0.1, 0.2));
            var updates = _rng.Next(0, 6);
            activeInstalls += installs - uninstalls;

            installCmd.Parameters[0].Value = package;
            installCmd.Parameters[1].Value = dateStr;
            installCmd.Parameters[2].Value = installs;
            installCmd.Parameters[3].Value = uninstalls;
            installCmd.Parameters[4].Value = updates;
            installCmd.Parameters[5].Value = activeInstalls;
            installCmd.Parameters[6].Value = nowStr;
            installCmd.ExecuteNonQuery();
            records++;

            var purchases = RangeDouble(0.0, 1.0) > 0.2 ? _rng.Next(1, 5) : 0;
            var revenue = purchases * 5.99;
            revenueCmd.Parameters[0].Value = package;
            revenueCmd.Parameters[1].Value = dateStr;
            revenueCmd.Parameters[2].Value = purchases;
            revenueCmd.Parameters[3].Value = revenue;
            revenueCmd.Parameters[4].Value = nowStr;
            revenueCmd.ExecuteNonQuery();
            records++;

            var newRatings = _rng.Next(0, 4);
            totalRatings += newRatings;
            var avgRating = 4.3 + RangeDouble(0.0, 0.5);
            var star5 = (long)(totalRatings * 0.55);
            var star4 = (long)(totalRatings * 0.25);
            var star3 = (long)(totalRatings * 0.10);
            var star2 = (long)(totalRatings * 0.05);
            var star1 = totalRatings - star5 - star4 - star3 - star2;

            ratingCmd.Parameters[0].Value = package;
            ratingCmd.Parameters[1].Value = dateStr;
            ratingCmd.Parameters[2].Value = totalRatings;
            ratingCmd.Parameters[3].Value = avgRating;
            ratingCmd.Parameters[4].Value = star1;
            ratingCmd.Parameters[5].Value = star2;
            ratingCmd.Parameters[6].Value = star3;
            ratingCmd.Parameters[7].Value = star4;
            ratingCmd.Parameters[8].Value = star5;
            ratingCmd.Parameters[9].Value = nowStr;
            ratingCmd.ExecuteNonQuery();
            records++;

            var crashRate = RangeDouble(0.1, 0.4);
            var anrRate = RangeDouble(0.05, 0.15);
            crashCmd.Parameters[0].Value = package;
            crashCmd.Parameters[1].Value = dateStr;
            crashCmd.Parameters[2].Value = crashRate;
            crashCmd.Parameters[3].Value = anrRate;
            crashCmd.Parameters[4].Value = nowStr;
            crashCmd.ExecuteNonQuery();
            records++;
        }

        return records;
    }

    private static void UpdateConnectionSyncTimes(SqliteConnection connection)
    {
        var nowStr = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE connections SET last_sync = $ts;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$ts";
        p.Value = nowStr;
        cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    private double RangeDouble(double min, double max) => min + _rng.NextDouble() * (max - min);

    private int WeightedRandom(double[] weights)
    {
        var total = 0.0;
        foreach (var w in weights) total += w;
        var r = _rng.NextDouble() * total;
        for (var i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (r <= 0.0) return i;
        }
        return weights.Length - 1;
    }

    private static void AddParam(SqliteCommand cmd, string name)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        cmd.Parameters.Add(p);
    }
}
