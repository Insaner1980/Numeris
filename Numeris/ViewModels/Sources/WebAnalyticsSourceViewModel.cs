using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;

namespace Numeris.ViewModels.Sources;

public partial class WebAnalyticsSourceViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly WebAnalyticsSyncService _sync;

    [ObservableProperty] public partial WebAnalyticsConnectionInfo? Connection { get; set; }
    [ObservableProperty] public partial ObservableCollection<WebAnalyticsSite> Sites { get; set; } = new();
    [ObservableProperty] public partial string NewAccountId { get; set; } = "";
    [ObservableProperty] public partial string NewToken { get; set; } = "";
    [ObservableProperty] public partial string NewDomain { get; set; } = "";
    [ObservableProperty] public partial string NewSiteTag { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial int SyncDays { get; set; } = 30;

    public WebAnalyticsSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        WebAnalyticsSyncService sync)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _sync = sync;
    }

    public async Task LoadAsync()
    {
        Connection = await _connectionsRepo.GetWebAnalyticsAsync();
        if (Connection is { AccountId.Length: > 0 })
        {
            NewAccountId = Connection.AccountId;
        }
        await LoadSitesAsync();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(NewAccountId)) { StatusMessage = "Account ID is required"; return; }
        IsBusy = true;
        StatusMessage = "Saving...";
        try
        {
            var accountId = NewAccountId.Trim();
            var token = CloudflareSourceViewModel.NormalizeCloudflareToken(NewToken);
            if (!string.IsNullOrEmpty(token))
            {
                _vault.SetWebAnalyticsToken(accountId, token);
            }
            else if (string.IsNullOrEmpty(_vault.GetWebAnalyticsToken(accountId)))
            {
                StatusMessage = "API token is required on first save";
                return;
            }
            await _connectionsRepo.UpsertWebAnalyticsAsync(
                new WebAnalyticsConnectionConfig
                {
                    AccountId = accountId,
                    LastValidatedAt = _connectionsRepo.FormatNow(),
                },
                "configured");
            await LoadAsync();
            NewToken = "";
            StatusMessage = "Saved. Add a site tag mapping, then press Sync. Discover sites is optional.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DiscoverSitesAsync()
    {
        if (Connection is null || !Connection.HasToken)
        {
            StatusMessage = "Save credentials first";
            return;
        }
        IsBusy = true;
        StatusMessage = "Discovering sites...";
        try
        {
            var sites = await _sync.DiscoverSitesAsync(Connection.AccountId);
            ReplaceSites(sites);
            StatusMessage = $"Found {sites.Count} site(s)";
        }
        catch (Exception ex)
        {
            var message = ApiErrorMessage.Sanitize(ex);
            await LoadSitesAsync();
            StatusMessage = Sites.Count > 0
                ? $"Discovery failed: {message} Saved mappings can still sync."
                : $"Discovery failed: {message} Add the Web Analytics site tag manually below.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task TestAsync()
    {
        if (Connection is null || string.IsNullOrWhiteSpace(Connection.AccountId))
        {
            StatusMessage = "Save credentials first";
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing Web Analytics...";
        try
        {
            var result = await _sync.TestAccountAsync(Connection.AccountId);
            StatusMessage = result.Ok ? result.Message : $"Failed: {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task AddSiteAsync()
    {
        IsBusy = true;
        StatusMessage = "Saving site tag...";
        try
        {
            var sites = await _sync.AddManualSiteAsync(NewDomain, NewSiteTag);
            ReplaceSites(sites);
            NewDomain = "";
            NewSiteTag = "";
            StatusMessage = $"Saved {sites.Count} site mapping(s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save site failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSiteAsync(WebAnalyticsSite? site)
    {
        if (site is null) return;
        var sites = await _sync.DeleteSiteAsync(site.Host);
        ReplaceSites(sites);
        StatusMessage = $"Removed {site.Host}";
    }

    [RelayCommand]
    public async Task SyncAsync()
    {
        if (Connection is null || !Connection.HasToken)
        {
            StatusMessage = "Save credentials first";
            return;
        }
        IsBusy = true;
        StatusMessage = "Syncing...";
        try
        {
            if (Sites.Count == 0 && HasPendingSite())
            {
                var sites = await _sync.AddManualSiteAsync(NewDomain, NewSiteTag);
                ReplaceSites(sites);
                NewDomain = "";
                NewSiteTag = "";
            }

            var result = await _sync.SyncAccountAsync(Connection.AccountId, SyncDays);
            StatusMessage = $"Synced {result.Domain}, {result.RecordsUpserted} rows";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (Connection is null) return;
        if (!string.IsNullOrEmpty(Connection.AccountId))
        {
            _vault.DeleteWebAnalyticsToken(Connection.AccountId);
        }
        await _connectionsRepo.DeleteWebAnalyticsAsync();
        await LoadAsync();
        StatusMessage = "Removed";
    }

    private async Task LoadSitesAsync()
    {
        ReplaceSites(await _sync.ListSavedSitesAsync());
    }

    private void ReplaceSites(System.Collections.Generic.IEnumerable<WebAnalyticsSite> sites)
    {
        Sites.Clear();
        foreach (var site in sites) Sites.Add(site);
    }

    private bool HasPendingSite()
        => !string.IsNullOrWhiteSpace(NewDomain) && !string.IsNullOrWhiteSpace(NewSiteTag);
}
