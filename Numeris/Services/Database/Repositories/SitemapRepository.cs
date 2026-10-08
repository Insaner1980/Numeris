using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;
using Numeris.Services.Api;

namespace Numeris.Services.Database.Repositories;

public sealed class SitemapRepository
{
    private readonly SqliteDatabase _db;

    public SitemapRepository(SqliteDatabase db) => _db = db;

    public Task<List<SitemapUrl>> ListUrlsAsync(string domain)
    {
        return _db.ReadAsync(connection =>
        {
            const string sql = """
                SELECT domain AS Domain,
                       url AS Url,
                       discovered_at AS DiscoveredAt,
                       last_seen_at AS LastSeenAt,
                       removed_at AS RemovedAt,
                       verdict AS Verdict,
                       coverage_state AS CoverageState,
                       indexing_state AS IndexingState,
                       last_inspected_at AS LastInspectedAt,
                       last_crawl_time AS LastCrawlTime,
                       page_fetch_state AS PageFetchState,
                       crawled_as AS CrawledAs
                FROM sitemap_urls
                WHERE (@domain = 'all' OR domain = @domain)
                ORDER BY domain, url
                """;
            return new List<SitemapUrl>(connection.Query<SitemapUrl>(sql, new { domain }));
        });
    }

    public Task UpdateInspectionAsync(string domain, UrlInspectionData inspection)
    {
        return _db.WriteAsync(connection =>
        {
            var nowStr = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            connection.Execute(
                """
                UPDATE sitemap_urls
                SET last_inspected_at = @nowStr,
                    verdict = @verdict,
                    coverage_state = @coverageState,
                    indexing_state = @indexingState,
                    robots_txt_state = @robotsTxtState,
                    page_fetch_state = @pageFetchState,
                    crawled_as = @crawledAs,
                    last_crawl_time = @lastCrawlTime
                WHERE domain = @domain AND url = @url
                """,
                new
                {
                    nowStr,
                    domain,
                    url = inspection.Url,
                    verdict = inspection.Verdict,
                    coverageState = inspection.CoverageState,
                    indexingState = inspection.IndexingState,
                    robotsTxtState = inspection.RobotsTxtState,
                    pageFetchState = inspection.PageFetchState,
                    crawledAs = inspection.CrawledAs,
                    lastCrawlTime = inspection.LastCrawlTime,
                });
        });
    }
}
