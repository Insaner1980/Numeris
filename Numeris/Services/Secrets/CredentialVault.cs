using System;
using System.Linq;
using Numeris.Helpers;
using Windows.Security.Credentials;

namespace Numeris.Services.Secrets;

public sealed class CredentialVault
{
    private const string DefaultAccount = "default";

    private const int ElementNotFound = unchecked((int)0x80070490);
    private const string CloudflareResource = "Numeris.Cloudflare";
    private const string WebAnalyticsResource = "Numeris.WebAnalytics";
    private const string SearchConsoleSecretResource = "Numeris.SearchConsole.ClientSecret";
    private const string SearchConsoleRefreshResource = "Numeris.SearchConsole.RefreshToken";
    private const string CruxApiKeyResource = "Numeris.Crux.ApiKey";
    private const string PageSpeedApiKeyResource = "Numeris.PageSpeed.ApiKey";
    private const string BingApiKeyResource = "Numeris.BingWebmaster.ApiKey";

    private readonly PasswordVault? _vault;
    private readonly Func<string, string, string> _readPassword;
    private readonly Action<string, string, string>? _savePassword;
    private readonly Action<string, string>? _deletePassword;

    public CredentialVault()
    {
        _vault = new PasswordVault();
        _readPassword = ReadPassword;
    }

    internal CredentialVault(Func<string, string, string> readPassword)
    {
        _readPassword = readPassword;
    }

    internal CredentialVault(Func<string, string, string> readPassword,
        Action<string, string, string> savePassword, Action<string, string> deletePassword)
        : this(readPassword)
    {
        _savePassword = savePassword;
        _deletePassword = deletePassword;
    }

    private PasswordVault Vault => _vault
        ?? throw new InvalidOperationException("Credential writes are unavailable for an isolated read-only vault");

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
        => Save(CruxApiKeyResource, DefaultAccount, key.Trim());

    public string? GetCruxApiKey()
        => TryGet(CruxApiKeyResource, DefaultAccount);

    public void DeleteCruxApiKey()
        => Delete(CruxApiKeyResource, DefaultAccount);

    public void SetPageSpeedApiKey(string key)
        => Save(PageSpeedApiKeyResource, DefaultAccount, key.Trim());

    public string? GetPageSpeedApiKey()
        => TryGet(PageSpeedApiKeyResource, DefaultAccount);

    public void DeletePageSpeedApiKey()
        => Delete(PageSpeedApiKeyResource, DefaultAccount);

    public void SetBingApiKey(string key)
        => Save(BingApiKeyResource, DefaultAccount, key.Trim());

    public string? GetBingApiKey()
        => TryGet(BingApiKeyResource, DefaultAccount);

    public void DeleteBingApiKey()
        => Delete(BingApiKeyResource, DefaultAccount);

    public void RemoveRetiredCredentials()
    {
        System.Collections.Generic.IReadOnlyList<PasswordCredential> credentials;
        try { credentials = Vault.RetrieveAll(); }
        catch (Exception ex) when (ex.HResult == ElementNotFound) { return; }
        foreach (var credential in credentials.Where(credential => credential.Resource is "Numeris.GoogleAnalytics.ClientSecret" or "Numeris.GoogleAnalytics.RefreshToken"))
        {
            Vault.Remove(credential);
        }
    }

    private void Save(string resource, string user, string secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
        {
            if (_savePassword is not null) _savePassword(resource, user, secret.Trim());
            else Vault.Add(new PasswordCredential(resource, user, secret.Trim()));
        }
        else
        {
            Delete(resource, user);
        }
    }

    private string? TryGet(string resource, string user)
    {
        try
        {
            return _readPassword(resource, user);
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not read saved credentials. Check Windows Credential Manager and try again.", ex);
        }
    }

    private string ReadPassword(string resource, string user)
    {
        var credential = Vault.Retrieve(resource, user);
        credential.RetrievePassword();
        return credential.Password;
    }

    private void Delete(string resource, string user)
    {
        if (_deletePassword is not null)
        {
            _deletePassword(resource, user);
            return;
        }
        try
        {
            var existing = Vault.Retrieve(resource, user);
            Vault.Remove(existing);
        }
        catch (Exception ex) when (ex.HResult == ElementNotFound)
        {
            // not present
        }
    }

    private static string NormalizeDomain(string domain) => SiteIdentity.NormalizeDomain(domain);
}
