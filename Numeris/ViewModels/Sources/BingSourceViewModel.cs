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
    private int _loadVersion;
    private readonly CredentialVault _vault;
    private readonly BingWebmasterSyncService _sync;

    [ObservableProperty] public partial BingConnectionInfo? Connection { get; set; }
    [ObservableProperty] public partial ObservableCollection<string> Sites { get; set; } = new();
    [ObservableProperty] public partial string NewApiKey { get; set; } = "";
    [ObservableProperty] public partial string NewSiteUrl { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    public bool CanRun => !IsBusy;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public BingSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        BingWebmasterSyncService sync)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _sync = sync;
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public async Task LoadAsync()
    {
        var loadVersion = ++_loadVersion;
        var connection = await _connectionsRepo.GetBingAsync();
        var sites = await _sync.ListSitesAsync();
        if (loadVersion != _loadVersion) return;
        Connection = connection;
        Sites.Clear();
        foreach (var site in sites) Sites.Add(site);
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsBusy) return;
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
            await LoadAsync();
            NewApiKey = "";
            StatusMessage = "Saved. Press Test or use Bing page refresh.";
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
        if (IsBusy) return;
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
            await LoadAsync();
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
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(siteUrl)) return;
            await _sync.DeleteSiteAsync(siteUrl);
            await SaveConfigFromSitesAsync();
            await LoadAsync();
            StatusMessage = $"Removed {siteUrl}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task TestAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Testing Bing Webmaster...";
        try
        {
            var result = await _sync.TestAsync();
            StatusMessage = result.Ok ? result.Message : $"Failed: {result.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Action failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _vault.DeleteBingApiKey();
            await _connectionsRepo.DeleteBingAsync();
            NewApiKey = "";
            await LoadAsync();
            StatusMessage = "Removed";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveConfigFromSitesAsync()
    {
        var sites = (await _sync.ListSitesAsync()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _connectionsRepo.UpsertBingAsync(
            new BingConnectionConfig { Sites = sites, LastValidatedAt = ConnectionsRepository.FormatNow() },
            string.IsNullOrWhiteSpace(_vault.GetBingApiKey()) ? "disconnected" : "configured");
    }
}
