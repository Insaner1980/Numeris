using System;
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

namespace Numeris.ViewModels.Sources;

public partial class SearchConsoleSourceViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly GoogleOAuthFlow _oauth;
    private readonly SearchConsoleSyncService _sync;

    [ObservableProperty] public partial SearchConsoleConnectionInfo? Connection { get; set; }
    [ObservableProperty] public partial string NewClientId { get; set; } = "";
    [ObservableProperty] public partial string NewClientSecret { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    public bool CanRun => !IsBusy;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    [ObservableProperty] public partial int SyncDays { get; set; } = 30;

    public SearchConsoleSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        GoogleOAuthFlow oauth,
        SearchConsoleSyncService sync)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _oauth = oauth;
        _sync = sync;
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public async Task LoadAsync()
    {
        Connection = await _connectionsRepo.GetSearchConsoleAsync();
        if (Connection is { ClientId.Length: > 0 })
        {
            NewClientId = Connection.ClientId;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(NewClientId))
        {
            StatusMessage = "Client ID is required";
            return;
        }
        IsBusy = true;
        StatusMessage = "Saving...";
        try
        {
            var clientId = NewClientId.Trim();
            var secret = NewClientSecret.Trim();
            if (!string.IsNullOrEmpty(secret))
            {
                _vault.SetSearchConsoleClientSecret(clientId, secret);
            }
            else if (string.IsNullOrEmpty(_vault.GetSearchConsoleClientSecret(clientId)))
            {
                StatusMessage = "Client secret is required on first save";
                return;
            }
            await _connectionsRepo.UpsertSearchConsoleAsync(
                new SearchConsoleConnectionConfig
                {
                    ClientId = clientId,
                    LastValidatedAt = _connectionsRepo.FormatNow(),
                },
                "configured");
            await LoadAsync();
            NewClientSecret = "";
            StatusMessage = "Saved. Press Connect Google account to authorize.";
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
    public async Task ConnectAsync()
    {
        if (Connection is null || string.IsNullOrEmpty(Connection.ClientId) || !Connection.HasClientSecret)
        {
            StatusMessage = "Save client ID and secret first";
            return;
        }
        IsBusy = true;
        StatusMessage = "Opening browser for Google consent...";
        try
        {
            var clientSecret = _vault.GetSearchConsoleClientSecret(Connection.ClientId)
                ?? throw new InvalidOperationException("Missing client secret");
            var tokens = await _oauth.AuthorizeAsync(
                Connection.ClientId,
                clientSecret,
                uri => _ = Launcher.LaunchUriAsync(uri),
                new[] { SearchConsoleClient.Scope });
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
            {
                _vault.SetSearchConsoleRefreshToken(Connection.ClientId, tokens.RefreshToken);
            }
            await LoadAsync();
            StatusMessage = "Authorized. Press Sync to fetch live data.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Authorization failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task TestAsync()
    {
        if (Connection is null || string.IsNullOrWhiteSpace(Connection.ClientId))
        {
            StatusMessage = "Save Search Console credentials first";
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing Search Console...";
        try
        {
            var result = await _sync.TestAsync(Connection.ClientId);
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
        if (Connection is null || !Connection.HasRefreshToken)
        {
            StatusMessage = "Connect Google account first";
            return;
        }
        IsBusy = true;
        StatusMessage = "Syncing...";
        try
        {
            var result = await _sync.SyncAsync(Connection.ClientId, SyncDays);
            StatusMessage = $"Synced {result.RecordsUpserted} rows";
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
        if (!string.IsNullOrEmpty(Connection.ClientId))
        {
            _vault.DeleteSearchConsoleClientSecret(Connection.ClientId);
            _vault.DeleteSearchConsoleRefreshToken(Connection.ClientId);
        }
        await _connectionsRepo.DeleteSearchConsoleAsync();
        await LoadAsync();
        StatusMessage = "Removed";
    }
}
