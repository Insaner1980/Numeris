using System;
using Windows.Security.Credentials;

namespace Numeris.Services.Secrets;

public sealed class CredentialVault
{
    private const string CloudflareResource = "Numeris.Cloudflare";
    private const string WebAnalyticsResource = "Numeris.WebAnalytics";
    private const string SearchConsoleSecretResource = "Numeris.SearchConsole.ClientSecret";
    private const string SearchConsoleRefreshResource = "Numeris.SearchConsole.RefreshToken";

    private readonly PasswordVault _vault = new();

    public void SetCloudflareToken(string domain, string token)
        => Save(CloudflareResource, NormalizeDomain(domain), token);

    public string? GetCloudflareToken(string domain)
        => TryGet(CloudflareResource, NormalizeDomain(domain));

    public void DeleteCloudflareToken(string domain)
        => Delete(CloudflareResource, NormalizeDomain(domain));

    public void SetWebAnalyticsToken(string accountId, string token)
        => Save(WebAnalyticsResource, accountId.Trim(), token);

    public string? GetWebAnalyticsToken(string accountId)
        => TryGet(WebAnalyticsResource, accountId.Trim());

    public void SetSearchConsoleClientSecret(string clientId, string secret)
        => Save(SearchConsoleSecretResource, clientId.Trim(), secret);

    public string? GetSearchConsoleClientSecret(string clientId)
        => TryGet(SearchConsoleSecretResource, clientId.Trim());

    public void SetSearchConsoleRefreshToken(string clientId, string token)
        => Save(SearchConsoleRefreshResource, clientId.Trim(), token);

    public string? GetSearchConsoleRefreshToken(string clientId)
        => TryGet(SearchConsoleRefreshResource, clientId.Trim());

    private void Save(string resource, string user, string secret)
    {
        try
        {
            var existing = _vault.Retrieve(resource, user);
            _vault.Remove(existing);
        }
        catch
        {
            // entry didn't exist — fine
        }
        _vault.Add(new PasswordCredential(resource, user, secret));
    }

    private string? TryGet(string resource, string user)
    {
        try
        {
            var cred = _vault.Retrieve(resource, user);
            cred.RetrievePassword();
            return cred.Password;
        }
        catch
        {
            return null;
        }
    }

    private void Delete(string resource, string user)
    {
        try
        {
            var existing = _vault.Retrieve(resource, user);
            _vault.Remove(existing);
        }
        catch
        {
            // not present
        }
    }

    private static string NormalizeDomain(string domain) => domain.Trim().ToLowerInvariant();
}
