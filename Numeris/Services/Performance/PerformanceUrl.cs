using System;

namespace Numeris.Services.Performance;

public static class PerformanceUrl
{
    public static string NormalizeOrigin(string value)
    {
        var uri = ParseAbsoluteHttpsUrl(value);
        return $"{uri.Scheme}://{uri.Host}".ToLowerInvariant();
    }

    public static string NormalizePageUrl(string value)
    {
        var uri = ParseAbsoluteHttpsUrl(value);
        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Query = uri.Query.TrimStart('?'),
        };
        if (!builder.Path.EndsWith("/", StringComparison.Ordinal) && !uri.AbsolutePath.Split('/')[^1].Contains('.', StringComparison.Ordinal))
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
