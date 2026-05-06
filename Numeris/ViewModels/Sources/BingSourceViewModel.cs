using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;

namespace Numeris.ViewModels.Sources;

public partial class BingSourceViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly BingWebmasterSyncService _sync;

    [ObservableProperty] public partial BingConnectionInfo? Connection { get; set; }
    [ObservableProperty] public partial ObservableCollection<string> Sites { get; set; } = new();
    [ObservableProperty] public partial string NewApiKey { get; set; } = "";
    [ObservableProperty] public partial string NewSiteUrl { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public BingSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        BingWebmasterSyncService sync)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _sync = sync;
    }

    public async Task LoadAsync()
    {
        Connection = await _connectionsRepo.GetBingAsync();
        await LoadSitesAsync();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        IsBusy = true;
        StatusMessage = "Saving...";
        try
        {
            if (!string.IsNullOrWhiteSpace(NewApiKey))
            {
                _vault.SetBingApiKey(NewApiKey);
            }
            if (string.IsNullOrWhiteSpace(_vault.GetBingApiKey()))
            {
                StatusMessage = "Bing Webmaster API key is required";
                return;
            }
            await SaveConfigFromSitesAsync();
            NewApiKey = "";
            await LoadAsync();
            StatusMessage = "Saved. Press Test or Sync.";
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
    public async Task AddSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSiteUrl))
        {
            StatusMessage = "Site URL is required";
            return;
        }
        IsBusy = true;
        try
        {
            await _sync.AddSiteAsync(NewSiteUrl);
            NewSiteUrl = "";
            await SaveConfigFromSitesAsync();
            await LoadSitesAsync();
            StatusMessage = "Bing site added";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add site failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSiteAsync(string? siteUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl)) return;
        await _sync.DeleteSiteAsync(siteUrl);
        await SaveConfigFromSitesAsync();
        await LoadSitesAsync();
        StatusMessage = $"Removed {siteUrl}";
    }

    [RelayCommand]
    public async Task TestAsync()
    {
        IsBusy = true;
        StatusMessage = "Testing Bing Webmaster...";
        try
        {
            var result = await _sync.TestAsync();
            StatusMessage = result.Ok ? result.Message : $"Failed: {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SyncAsync()
    {
        IsBusy = true;
        StatusMessage = "Syncing Bing Webmaster...";
        try
        {
            var result = await _sync.SyncAsync();
            StatusMessage = $"Synced {result.SitesSynced} site(s), {result.RawItems} raw item(s), {result.RankRows} rank rows, {result.QueryRows} query rows, {result.PageRows} page rows";
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
        _vault.DeleteBingApiKey();
        await _connectionsRepo.DeleteBingAsync();
        NewApiKey = "";
        await LoadAsync();
        StatusMessage = "Removed";
    }

    private async Task LoadSitesAsync()
    {
        var sites = await _sync.ListSitesAsync();
        Sites.Clear();
        foreach (var site in sites) Sites.Add(site);
    }

    private async Task SaveConfigFromSitesAsync()
    {
        var sites = (await _sync.ListSitesAsync()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _connectionsRepo.UpsertBingAsync(
            new BingConnectionConfig { Sites = sites, LastValidatedAt = _connectionsRepo.FormatNow() },
            string.IsNullOrWhiteSpace(_vault.GetBingApiKey()) ? "mock" : "configured");
    }
}
