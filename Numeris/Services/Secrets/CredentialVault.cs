using System;
using Windows.Security.Credentials;

namespace Numeris.Services.Secrets;

public sealed class CredentialVault
{
    private const string CloudflareResource = "Numeris.Cloudflare";
    private const string WebAnalyticsResource = "Numeris.WebAnalytics";
    private const string SearchConsoleSecretResource = "Numeris.SearchConsole.ClientSecret";
    private const string SearchConsoleRefreshResource = "Numeris.SearchConsole.RefreshToken";
    private const string CruxApiKeyResource = "Numeris.Crux.ApiKey";
    private const string PageSpeedApiKeyResource = "Numeris.PageSpeed.ApiKey";
    private const string BingApiKeyResource = "Numeris.BingWebmaster.ApiKey";
    private const string YouTubeSecretResource = "Numeris.YouTube.ClientSecret";
    private const string YouTubeRefreshResource = "Numeris.YouTube.RefreshToken";

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

    public void DeleteWebAnalyticsToken(string accountId)
        => Delete(WebAnalyticsResource, accountId.Trim());

    public void SetSearchConsoleClientSecret(string clientId, string secret)
        => Save(SearchConsoleSecretResource, clientId.Trim(), secret);

    public string? GetSearchConsoleClientSecret(string clientId)
        => TryGet(SearchConsoleSecretResource, clientId.Trim());

    public void DeleteSearchConsoleClientSecret(string clientId)
        => Delete(SearchConsoleSecretResource, clientId.Trim());

    public void SetSearchConsoleRefreshToken(string clientId, string token)
        => Save(SearchConsoleRefreshResource, clientId.Trim(), token);

    public string? GetSearchConsoleRefreshToken(string clientId)
        => TryGet(SearchConsoleRefreshResource, clientId.Trim());

    public void DeleteSearchConsoleRefreshToken(string clientId)
        => Delete(SearchConsoleRefreshResource, clientId.Trim());

    public void SetCruxApiKey(string key)
        => Save(CruxApiKeyResource, "default", key.Trim());

    public string? GetCruxApiKey()
        => TryGet(CruxApiKeyResource, "default");

    public void DeleteCruxApiKey()
        => Delete(CruxApiKeyResource, "default");

    public void SetPageSpeedApiKey(string key)
        => Save(PageSpeedApiKeyResource, "default", key.Trim());

    public string? GetPageSpeedApiKey()
        => TryGet(PageSpeedApiKeyResource, "default");

    public void DeletePageSpeedApiKey()
        => Delete(PageSpeedApiKeyResource, "default");

    public void SetBingApiKey(string key)
        => Save(BingApiKeyResource, "default", key.Trim());

    public string? GetBingApiKey()
        => TryGet(BingApiKeyResource, "default");

    public void DeleteBingApiKey()
        => Delete(BingApiKeyResource, "default");

    public void SetYouTubeClientSecret(string clientId, string secret)
        => Save(YouTubeSecretResource, clientId.Trim(), secret);

    public string? GetYouTubeClientSecret(string clientId)
        => TryGet(YouTubeSecretResource, clientId.Trim());

    public void DeleteYouTubeClientSecret(string clientId)
        => Delete(YouTubeSecretResource, clientId.Trim());

    public void SetYouTubeRefreshToken(string clientId, string token)
        => Save(YouTubeRefreshResource, clientId.Trim(), token);

    public string? GetYouTubeRefreshToken(string clientId)
        => TryGet(YouTubeRefreshResource, clientId.Trim());

    public void DeleteYouTubeRefreshToken(string clientId)
        => Delete(YouTubeRefreshResource, clientId.Trim());

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
        if (!string.IsNullOrWhiteSpace(secret))
        {
            _vault.Add(new PasswordCredential(resource, user, secret.Trim()));
        }
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
