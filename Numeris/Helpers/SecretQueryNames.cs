using System;

namespace Numeris.Helpers;

internal static class SecretQueryNames
{
    public static bool IsSecret(string? rawName)
    {
        var name = rawName is null ? null : Uri.UnescapeDataString(rawName)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return name is "key" or "apikey" or "accesstoken" or "refreshtoken" or "idtoken" or "token"
            or "clientsecret" or "secret" or "password" or "authorization" or "code";
    }
}
