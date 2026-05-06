using System;
using System.Text.RegularExpressions;

namespace Numeris.Helpers;

public sealed record SiteIdentity(string Domain, string OriginUrl, string HomePageUrl)
{
    private static readonly Regex HostPattern = new(
        @"^(?=.{1,253}$)(?!-)([a-z0-9-]{1,63}\.)+[a-z]{2,63}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static SiteIdentity FromDomainOrUrl(string value)
    {
        var domain = NormalizeDomain(value);
        return new SiteIdentity(domain, $"https://{domain}", $"https://{domain}/");
    }

    public static string NormalizeDomain(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Domain or URL is required", nameof(value));
        }

        string host;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new ArgumentException("URL must use http:// or https:// and include a host", nameof(value));
            }
            host = uri.Host;
        }
        else
        {
            host = trimmed.TrimEnd('/');
            if (host.Contains('/', StringComparison.Ordinal) || host.Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException("Domain must be a bare host name", nameof(value));
            }
        }

        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (!HostPattern.IsMatch(host))
        {
            throw new ArgumentException("Domain must be a valid host name", nameof(value));
        }
        return host;
    }

    public static string NormalizeOriginUrl(string value)
        => FromDomainOrUrl(value).OriginUrl;

    public static string NormalizeHomePageUrl(string value)
        => FromDomainOrUrl(value).HomePageUrl;

    public static string NormalizeHttpsPageUrl(string value)
    {
        var uri = ParseAbsoluteHttpsUrl(value);
        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Query = uri.Query.TrimStart('?'),
        };

        if (!builder.Path.EndsWith("/", StringComparison.Ordinal)
            && !uri.AbsolutePath.Split('/')[^1].Contains('.', StringComparison.Ordinal))
        {
            builder.Path += "/";
        }

        return builder.Uri.ToString();
    }

    private static Uri ParseAbsoluteHttpsUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("URL must be an absolute https:// URL", nameof(value));
        }
        return uri;
    }
}
