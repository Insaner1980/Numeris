using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;

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
                       indexing_state AS IndexingState
                FROM sitemap_urls
                WHERE domain = @domain
                ORDER BY url
                """;
            return new List<SitemapUrl>(connection.Query<SitemapUrl>(sql, new { domain }));
        });
    }
}
