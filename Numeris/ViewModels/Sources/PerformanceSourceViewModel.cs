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

public partial class PerformanceSourceViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private int _loadVersion;
    private readonly CredentialVault _vault;
    private readonly PerformanceSyncService _sync;
    private readonly ShellViewModel _shell;

    [ObservableProperty] public partial PerformanceConnectionInfo? Connection { get; set; }
    [ObservableProperty] public partial ObservableCollection<PerformanceUrlInfo> Urls { get; set; } = new();
    [ObservableProperty] public partial string NewCruxApiKey { get; set; } = "";
    [ObservableProperty] public partial string NewPageSpeedApiKey { get; set; } = "";
    [ObservableProperty] public partial string NewUrl { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    public bool CanRun => !IsBusy;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public PerformanceSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        PerformanceSyncService sync,
        ShellViewModel shell)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _sync = sync;
        _shell = shell;
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public async Task LoadAsync()
    {
        var loadVersion = ++_loadVersion;
        var connection = await _connectionsRepo.GetPerformanceAsync();
        var urls = await _sync.ListUrlsAsync();
        await _shell.RefreshAvailableDomainsAsync();
        if (loadVersion != _loadVersion) return;
        Connection = connection;
        Urls.Clear();
        foreach (var url in urls) Urls.Add(url);
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Saving...";
        try
        {
            if (!string.IsNullOrWhiteSpace(NewCruxApiKey))
            {
                _vault.SetCruxApiKey(NewCruxApiKey);
            }
            if (!string.IsNullOrWhiteSpace(NewPageSpeedApiKey))
            {
                _vault.SetPageSpeedApiKey(NewPageSpeedApiKey);
            }
            if (string.IsNullOrWhiteSpace(_vault.GetCruxApiKey()) && string.IsNullOrWhiteSpace(_vault.GetPageSpeedApiKey()))
            {
                StatusMessage = "CrUX or PageSpeed API key is required";
                return;
            }
            await _connectionsRepo.UpsertPerformanceAsync(
                new PerformanceConnectionConfig { LastValidatedAt = ConnectionsRepository.FormatNow() },
                "configured");
            await LoadAsync();
            NewCruxApiKey = "";
            NewPageSpeedApiKey = "";
            StatusMessage = "Saved. Add URLs if needed; use Performance page refresh to fetch live data.";
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
    public async Task AddUrlAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(NewUrl))
        {
            StatusMessage = "URL is required";
            return;
        }
        IsBusy = true;
        try
        {
            await _sync.AddUrlAsync(NewUrl);
            NewUrl = "";
            await LoadUrlsAsync();
            StatusMessage = "URL added";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add URL failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteUrlAsync(PerformanceUrlInfo? url)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (url is null) return;
            await _sync.DeleteUrlAsync(url.Id);
            await LoadUrlsAsync();
            StatusMessage = $"Removed {url.Url}";
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
    public async Task TestCruxAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Testing CrUX...";
        try
        {
            var result = await _sync.TestCruxAsync();
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
    public async Task TestPageSpeedAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Testing PageSpeed...";
        try
        {
            var result = await _sync.TestPageSpeedAsync();
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
            _vault.DeleteCruxApiKey();
            _vault.DeletePageSpeedApiKey();
            await _connectionsRepo.DeletePerformanceAsync();
            NewCruxApiKey = "";
            NewPageSpeedApiKey = "";
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

    private async Task LoadUrlsAsync()
    {
        var urls = await _sync.ListUrlsAsync();
        await _shell.RefreshAvailableDomainsAsync();
        Urls.Clear();
        foreach (var url in urls) Urls.Add(url);
    }
}
