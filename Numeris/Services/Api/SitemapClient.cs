using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Numeris.Services.Api;

public sealed class SitemapClient
{
    private readonly HttpClient _http;

    public SitemapClient(HttpClient? http = null) => _http = http ?? HttpClient;

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
            throw new InvalidOperationException("No child sitemaps found in sitemap-index.xml");
        }

        var allUrls = new List<string>();
        foreach (var child in childUrls)
        {
            var xml = await HttpGetAsync(child).ConfigureAwait(false);
            allUrls.AddRange(ParseUrlset(xml));
        }
        return allUrls.Distinct().OrderBy(u => u, StringComparer.Ordinal).ToList();
    }

    public static List<string> ParseSitemapIndex(string xml) => ParseLocs(xml, "sitemapindex", "sitemap");

    public static List<string> ParseUrlset(string xml) => ParseLocs(xml, "urlset", "url");

    private static List<string> ParseLocs(string xml, string rootName, string itemName)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        if (root is null || (root.Name.Namespace != SitemapNs && root.Name.Namespace != XNamespace.None)
            || root.Name.LocalName != rootName)
        {
            throw new InvalidOperationException($"Unsupported sitemap document; expected {rootName}");
        }
        var locs = new List<string>();
        foreach (var loc in root.Elements(root.Name.Namespace + itemName).Elements(root.Name.Namespace + "loc"))
        {
            var value = loc.Value.Trim();
            if (value.Length > 0) locs.Add(value);
        }
        return locs;
    }

    private async Task<string> HttpGetAsync(string url)
    {
        using var response = await _http.GetAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("Sitemap", "fetch", response.StatusCode);
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
