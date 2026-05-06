using Numeris.Helpers;

namespace Numeris.Services.Performance;

public static class PerformanceUrl
{
    public static string NormalizeOrigin(string value)
        => SiteIdentity.NormalizeOriginUrl(value);

    public static string NormalizePageUrl(string value)
        => SiteIdentity.NormalizeHttpsPageUrl(value);
}
