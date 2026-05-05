using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Numeris.Services.Api;

public sealed class SitemapClient
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public async Task<List<string>> DiscoverUrlsAsync(string domain)
    {
        var normalized = NormalizeDomain(domain);
        var indexUrl = $"https://{normalized}/sitemap-index.xml";
        var indexXml = await HttpGetAsync(indexUrl).ConfigureAwait(false);
        var childUrls = ParseSitemapIndex(indexXml);
        if (childUrls.Count == 0)
        {
            throw new InvalidOperationException($"No child sitemaps found in {indexUrl}");
        }

        var allUrls = new List<string>();
        foreach (var child in childUrls)
        {
            var xml = await HttpGetAsync(child).ConfigureAwait(false);
            allUrls.AddRange(ParseUrlset(xml));
        }
        return allUrls.Distinct().OrderBy(u => u, StringComparer.Ordinal).ToList();
    }

    public static List<string> ParseSitemapIndex(string xml) => ParseLocs(xml);

    public static List<string> ParseUrlset(string xml) => ParseLocs(xml);

    private static List<string> ParseLocs(string xml)
    {
        var doc = XDocument.Parse(xml);
        var locs = new List<string>();
        foreach (var loc in doc.Descendants(SitemapNs + "loc"))
        {
            var v = loc.Value?.Trim();
            if (!string.IsNullOrEmpty(v)) locs.Add(v);
        }
        if (locs.Count == 0)
        {
            // Fallback for sitemaps without namespace
            foreach (var loc in doc.Descendants("loc"))
            {
                var v = loc.Value?.Trim();
                if (!string.IsNullOrEmpty(v)) locs.Add(v);
            }
        }
        return locs;
    }

    private static async Task<string> HttpGetAsync(string url)
    {
        using var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to fetch {url}: HTTP {(int)response.StatusCode}");
        }
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    private static string NormalizeDomain(string domain)
    {
        var d = domain.Trim();
        if (d.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) d = d[8..];
        else if (d.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) d = d[7..];
        return d.TrimEnd('/');
    }
}
