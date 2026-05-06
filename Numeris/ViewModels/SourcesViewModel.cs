using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Windows.System;

namespace Numeris.ViewModels;

public partial class SourcesViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly CloudflareGraphqlClient _cfClient;
    private readonly CloudflareSyncService _cfSync;
    private readonly CloudflareRumClient _rumClient;
    private readonly WebAnalyticsSyncService _waSync;
    private readonly SearchConsoleClient _scClient;
    private readonly GoogleOAuthFlow _oauth;
    private readonly SearchConsoleSyncService _scSync;

    [ObservableProperty] private ObservableCollection<CloudflareConnectionInfo> _cloudflareConnections = new();

    [ObservableProperty] private string _newCfDomain = "";
    [ObservableProperty] private string _newCfZoneId = "";
    [ObservableProperty] private string _newCfToken = "";
    [ObservableProperty] private string _cfStatusMessage = "";
    [ObservableProperty] private bool _isCfBusy;
    [ObservableProperty] private int _cfSyncDays = 90;

    [ObservableProperty] private WebAnalyticsConnectionInfo? _webAnalytics;
    [ObservableProperty] private ObservableCollection<WebAnalyticsSite> _waSites = new();
    [ObservableProperty] private string _newWaAccountId = "";
    [ObservableProperty] private string _newWaToken = "";
    [ObservableProperty] private string _newWaDomain = "";
    [ObservableProperty] private string _newWaSiteTag = "";
    [ObservableProperty] private string _waStatusMessage = "";
    [ObservableProperty] private bool _isWaBusy;
    [ObservableProperty] private int _waSyncDays = 30;

    [ObservableProperty] private SearchConsoleConnectionInfo? _searchConsole;
    [ObservableProperty] private string _newScClientId = "";
    [ObservableProperty] private string _newScClientSecret = "";
    [ObservableProperty] private string _scStatusMessage = "";
    [ObservableProperty] private bool _isScBusy;
    [ObservableProperty] private int _scSyncDays = 30;

    public SourcesViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        CloudflareGraphqlClient cfClient,
        CloudflareSyncService cfSync,
        CloudflareRumClient rumClient,
        WebAnalyticsSyncService waSync,
        SearchConsoleClient scClient,
        GoogleOAuthFlow oauth,
        SearchConsoleSyncService scSync)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _cfClient = cfClient;
        _cfSync = cfSync;
        _rumClient = rumClient;
        _waSync = waSync;
        _scClient = scClient;
        _oauth = oauth;
        _scSync = scSync;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var list = await _connectionsRepo.ListCloudflareConnectionsAsync();
        CloudflareConnections.Clear();
        foreach (var c in list) CloudflareConnections.Add(c);

        WebAnalytics = await _connectionsRepo.GetWebAnalyticsAsync();
        if (WebAnalytics is { AccountId.Length: > 0 })
        {
            NewWaAccountId = WebAnalytics.AccountId;
        }
        await LoadWebAnalyticsSitesAsync();

        SearchConsole = await _connectionsRepo.GetSearchConsoleAsync();
        if (SearchConsole is { ClientId.Length: > 0 })
        {
            NewScClientId = SearchConsole.ClientId;
        }
    }

    [RelayCommand]
    public async Task TestCloudflareAsync()
    {
        if (!ValidateCloudflareInputs(requireToken: true)) return;
        IsCfBusy = true;
        CfStatusMessage = "Testing connection...";
        try
        {
            await _cfClient.ValidateZoneAsync(NormalizeCloudflareToken(NewCfToken), NewCfZoneId.Trim(), NewCfDomain.Trim().ToLowerInvariant());
            CfStatusMessage = $"OK — token validates {NewCfDomain.Trim().ToLowerInvariant()}";
        }
        catch (Exception ex)
        {
            CfStatusMessage = $"Failed: {ex.Message}";
        }
        finally
        {
            IsCfBusy = false;
        }
    }

    [RelayCommand]
    public async Task TestSavedCloudflareAsync(CloudflareConnectionInfo? info)
    {
        if (info is null) return;
        IsCfBusy = true;
        CfStatusMessage = $"Testing {info.Domain}...";
        try
        {
            var result = await _cfSync.TestDomainAsync(info.Domain, info.ZoneId);
            CfStatusMessage = result.Ok ? result.Message : $"Failed: {result.Message}";
        }
        finally
        {
            IsCfBusy = false;
        }
    }

    [RelayCommand]
    public async Task SaveCloudflareAsync()
    {
        if (!ValidateCloudflareInputs(requireToken: false)) return;
        IsCfBusy = true;
        CfStatusMessage = "Saving...";
        try
        {
            var domain = NewCfDomain.Trim().ToLowerInvariant();
            var zoneId = NewCfZoneId.Trim();
            var token = NormalizeCloudflareToken(NewCfToken);
            if (!string.IsNullOrEmpty(token))
            {
                _vault.SetCloudflareToken(domain, token);
            }
            else if (string.IsNullOrEmpty(_vault.GetCloudflareToken(domain)))
            {
                CfStatusMessage = "Token is required on first save";
                return;
            }

            var config = new CloudflareConnectionConfig
            {
                Domain = domain,
                ZoneId = zoneId,
                LastValidatedAt = _connectionsRepo.FormatNow(),
            };
            await _connectionsRepo.UpsertCloudflareAsync(config, "configured");
            await LoadAsync();

            NewCfToken = "";
            CfStatusMessage = $"Saved {domain}. Press Sync to fetch live data.";
        }
        catch (Exception ex)
        {
            CfStatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsCfBusy = false;
        }
    }

    [RelayCommand]
    public async Task SyncCloudflareAsync(CloudflareConnectionInfo? info)
    {
        if (info is null) return;
        IsCfBusy = true;
        CfStatusMessage = $"Syncing {info.Domain}...";
        try
        {
            var result = await _cfSync.SyncDomainAsync(info.Domain, info.ZoneId, CfSyncDays);
            CfStatusMessage = $"Synced {result.DaysSynced} days, {result.RecordsUpserted} rows";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            CfStatusMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsCfBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteCloudflareAsync(CloudflareConnectionInfo? info)
    {
        if (info is null) return;
        await _connectionsRepo.DeleteCloudflareAsync(info.Domain);
        _vault.DeleteCloudflareToken(info.Domain);
        await LoadAsync();
        CfStatusMessage = $"Removed {info.Domain}";
    }

    private bool ValidateCloudflareInputs(bool requireToken)
    {
        if (string.IsNullOrWhiteSpace(NewCfDomain)) { CfStatusMessage = "Domain is required"; return false; }
        if (string.IsNullOrWhiteSpace(NewCfZoneId)) { CfStatusMessage = "Zone ID is required"; return false; }
        if (requireToken && string.IsNullOrWhiteSpace(NewCfToken)) { CfStatusMessage = "API token is required"; return false; }
        return true;
    }

    [RelayCommand]
    public async Task SaveWebAnalyticsAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWaAccountId)) { WaStatusMessage = "Account ID is required"; return; }
        IsWaBusy = true;
        WaStatusMessage = "Saving...";
        try
        {
            var accountId = NewWaAccountId.Trim();
            var token = NormalizeCloudflareToken(NewWaToken);
            if (!string.IsNullOrEmpty(token))
            {
                _vault.SetWebAnalyticsToken(accountId, token);
            }
            else if (string.IsNullOrEmpty(_vault.GetWebAnalyticsToken(accountId)))
            {
                WaStatusMessage = "API token is required on first save";
                return;
            }
            var config = new WebAnalyticsConnectionConfig
            {
                AccountId = accountId,
                LastValidatedAt = _connectionsRepo.FormatNow(),
            };
            await _connectionsRepo.UpsertWebAnalyticsAsync(config, "configured");
            await LoadAsync();
            NewWaToken = "";
            WaStatusMessage = "Saved. Add a site tag mapping, then press Sync. Discover sites is optional.";
        }
        catch (Exception ex)
        {
            WaStatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsWaBusy = false;
        }
    }

    [RelayCommand]
    public async Task DiscoverWebAnalyticsSitesAsync()
    {
        if (WebAnalytics is null || !WebAnalytics.HasToken)
        {
            WaStatusMessage = "Save credentials first";
            return;
        }
        IsWaBusy = true;
        WaStatusMessage = "Discovering sites...";
        try
        {
            var sites = await _waSync.DiscoverSitesAsync(WebAnalytics.AccountId);
            WaSites.Clear();
            foreach (var s in sites) WaSites.Add(s);
            WaStatusMessage = $"Found {sites.Count} site(s)";
        }
        catch (Exception ex)
        {
            await LoadWebAnalyticsSitesAsync();
            WaStatusMessage = WaSites.Count > 0
                ? $"Discovery failed: {ex.Message} Saved mappings can still sync."
                : $"Discovery failed: {ex.Message} Add the Web Analytics site tag manually below.";
        }
        finally
        {
            IsWaBusy = false;
        }
    }

    [RelayCommand]
    public async Task TestWebAnalyticsAsync()
    {
        if (WebAnalytics is null || string.IsNullOrWhiteSpace(WebAnalytics.AccountId))
        {
            WaStatusMessage = "Save credentials first";
            return;
        }

        IsWaBusy = true;
        WaStatusMessage = "Testing Web Analytics...";
        try
        {
            var result = await _waSync.TestAccountAsync(WebAnalytics.AccountId);
            WaStatusMessage = result.Ok ? result.Message : $"Failed: {result.Message}";
        }
        finally
        {
            IsWaBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddWebAnalyticsSiteAsync()
    {
        IsWaBusy = true;
        WaStatusMessage = "Saving site tag...";
        try
        {
            var sites = await _waSync.AddManualSiteAsync(NewWaDomain, NewWaSiteTag);
            WaSites.Clear();
            foreach (var s in sites) WaSites.Add(s);
            NewWaDomain = "";
            NewWaSiteTag = "";
            WaStatusMessage = $"Saved {sites.Count} site mapping(s)";
        }
        catch (Exception ex)
        {
            WaStatusMessage = $"Save site failed: {ex.Message}";
        }
        finally
        {
            IsWaBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteWebAnalyticsSiteAsync(WebAnalyticsSite? site)
    {
        if (site is null) return;
        var sites = await _waSync.DeleteSiteAsync(site.Host);
        WaSites.Clear();
        foreach (var s in sites) WaSites.Add(s);
        WaStatusMessage = $"Removed {site.Host}";
    }

    [RelayCommand]
    public async Task SyncWebAnalyticsAsync()
    {
        if (WebAnalytics is null || !WebAnalytics.HasToken)
        {
            WaStatusMessage = "Save credentials first";
            return;
        }
        IsWaBusy = true;
        WaStatusMessage = "Syncing...";
        try
        {
            if (WaSites.Count == 0 && HasPendingWebAnalyticsSite())
            {
                var sites = await _waSync.AddManualSiteAsync(NewWaDomain, NewWaSiteTag);
                WaSites.Clear();
                foreach (var s in sites) WaSites.Add(s);
                NewWaDomain = "";
                NewWaSiteTag = "";
            }

            var result = await _waSync.SyncAccountAsync(WebAnalytics.AccountId, WaSyncDays);
            WaStatusMessage = $"Synced {result.Domain}, {result.RecordsUpserted} rows";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            WaStatusMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsWaBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteWebAnalyticsAsync()
    {
        if (WebAnalytics is null) return;
        if (!string.IsNullOrEmpty(WebAnalytics.AccountId))
        {
            _vault.SetWebAnalyticsToken(WebAnalytics.AccountId, "");
        }
        await _connectionsRepo.DeleteWebAnalyticsAsync();
        await LoadAsync();
        WaStatusMessage = "Removed";
    }

    private async Task LoadWebAnalyticsSitesAsync()
    {
        var sites = await _waSync.ListSavedSitesAsync();
        WaSites.Clear();
        foreach (var site in sites) WaSites.Add(site);
    }

    private bool HasPendingWebAnalyticsSite()
        => !string.IsNullOrWhiteSpace(NewWaDomain) && !string.IsNullOrWhiteSpace(NewWaSiteTag);

    private static string NormalizeCloudflareToken(string token)
    {
        token = token.Trim();
        const string bearerPrefix = "Bearer ";
        return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? token[bearerPrefix.Length..].Trim()
            : token;
    }

    [RelayCommand]
    public async Task SaveSearchConsoleAsync()
    {
        if (string.IsNullOrWhiteSpace(NewScClientId))
        {
            ScStatusMessage = "Client ID is required";
            return;
        }
        IsScBusy = true;
        ScStatusMessage = "Saving...";
        try
        {
            var clientId = NewScClientId.Trim();
            var secret = NewScClientSecret.Trim();
            if (!string.IsNullOrEmpty(secret))
            {
                _vault.SetSearchConsoleClientSecret(clientId, secret);
            }
            else if (string.IsNullOrEmpty(_vault.GetSearchConsoleClientSecret(clientId)))
            {
                ScStatusMessage = "Client secret is required on first save";
                return;
            }
            var config = new SearchConsoleConnectionConfig
            {
                ClientId = clientId,
                LastValidatedAt = _connectionsRepo.FormatNow(),
            };
            await _connectionsRepo.UpsertSearchConsoleAsync(config, "configured");
            await LoadAsync();
            NewScClientSecret = "";
            ScStatusMessage = "Saved. Press Connect Google account to authorize.";
        }
        catch (Exception ex)
        {
            ScStatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsScBusy = false;
        }
    }

    [RelayCommand]
    public async Task ConnectSearchConsoleAsync()
    {
        if (SearchConsole is null || string.IsNullOrEmpty(SearchConsole.ClientId) || !SearchConsole.HasClientSecret)
        {
            ScStatusMessage = "Save client ID and secret first";
            return;
        }
        IsScBusy = true;
        ScStatusMessage = "Opening browser for Google consent...";
        try
        {
            var clientSecret = _vault.GetSearchConsoleClientSecret(SearchConsole.ClientId)
                ?? throw new InvalidOperationException("Missing client secret");
            var tokens = await _oauth.AuthorizeAsync(
                SearchConsole.ClientId,
                clientSecret,
                uri => _ = Launcher.LaunchUriAsync(uri));
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
            {
                _vault.SetSearchConsoleRefreshToken(SearchConsole.ClientId, tokens.RefreshToken);
            }
            await LoadAsync();
            ScStatusMessage = "Authorized. Press Sync to fetch live data.";
        }
        catch (Exception ex)
        {
            ScStatusMessage = $"Authorization failed: {ex.Message}";
        }
        finally
        {
            IsScBusy = false;
        }
    }

    [RelayCommand]
    public async Task TestSearchConsoleAsync()
    {
        if (SearchConsole is null || string.IsNullOrWhiteSpace(SearchConsole.ClientId))
        {
            ScStatusMessage = "Save Search Console credentials first";
            return;
        }

        IsScBusy = true;
        ScStatusMessage = "Testing Search Console...";
        try
        {
            var result = await _scSync.TestAsync(SearchConsole.ClientId);
            ScStatusMessage = result.Ok ? result.Message : $"Failed: {result.Message}";
        }
        finally
        {
            IsScBusy = false;
        }
    }

    [RelayCommand]
    public async Task SyncSearchConsoleAsync()
    {
        if (SearchConsole is null || !SearchConsole.HasRefreshToken)
        {
            ScStatusMessage = "Connect Google account first";
            return;
        }
        IsScBusy = true;
        ScStatusMessage = "Syncing...";
        try
        {
            var result = await _scSync.SyncAsync(SearchConsole.ClientId, ScSyncDays);
            ScStatusMessage = $"Synced {result.RecordsUpserted} rows";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ScStatusMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsScBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSearchConsoleAsync()
    {
        if (SearchConsole is null) return;
        if (!string.IsNullOrEmpty(SearchConsole.ClientId))
        {
            _vault.SetSearchConsoleClientSecret(SearchConsole.ClientId, "");
            _vault.SetSearchConsoleRefreshToken(SearchConsole.ClientId, "");
        }
        await _connectionsRepo.DeleteSearchConsoleAsync();
        await LoadAsync();
        ScStatusMessage = "Removed";
    }
}
