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

public partial class YouTubeSourceViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly GoogleOAuthFlow _oauth;
    private readonly YouTubeSyncService _sync;

    [ObservableProperty] public partial YouTubeConnectionInfo? Connection { get; set; }
    [ObservableProperty] public partial string NewClientId { get; set; } = "";
    [ObservableProperty] public partial string NewClientSecret { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    public bool CanRun => !IsBusy;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    [ObservableProperty] public partial int SyncDays { get; set; } = 90;

    public YouTubeSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        GoogleOAuthFlow oauth,
        YouTubeSyncService sync)
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
        Connection = await _connectionsRepo.GetYouTubeAsync();
        if (Connection is { ClientId.Length: > 0 })
        {
            NewClientId = Connection.ClientId;
        }
        else
        {
            var googleConnection = await _connectionsRepo.GetSearchConsoleAsync();
            if (!string.IsNullOrWhiteSpace(googleConnection?.ClientId))
            {
                NewClientId = googleConnection.ClientId;
                StatusMessage = "Ready to connect with existing Google OAuth client";
            }
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
        StatusMessage = "Saving YouTube credentials...";
        try
        {
            var clientId = NewClientId.Trim();
            var secret = NewClientSecret.Trim();
            if (!string.IsNullOrEmpty(secret))
            {
                _vault.SetYouTubeClientSecret(clientId, secret);
            }
            else if (string.IsNullOrEmpty(ResolveClientSecret(clientId)))
            {
                StatusMessage = "Client secret is required unless Search Console already has this Google OAuth client";
                return;
            }

            await _connectionsRepo.UpsertYouTubeAsync(
                new YouTubeConnectionConfig
                {
                    ClientId = clientId,
                    ChannelId = Connection?.ChannelId ?? "",
                    ChannelTitle = Connection?.ChannelTitle ?? "",
                    LastValidatedAt = _connectionsRepo.FormatNow(),
                },
                "configured");
            NewClientSecret = "";
            await LoadAsync();
            StatusMessage = "Saved. Press Connect YouTube to authorize.";
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
        await EnsureConfiguredFromGoogleDefaultsAsync();
        if (Connection is null || string.IsNullOrWhiteSpace(Connection.ClientId) || string.IsNullOrWhiteSpace(ResolveClientSecret(Connection.ClientId)))
        {
            StatusMessage = "Configure Google OAuth credentials in Search Console or the advanced YouTube override first";
            return;
        }

        IsBusy = true;
        StatusMessage = "Opening browser for YouTube consent...";
        try
        {
            var clientSecret = ResolveClientSecret(Connection.ClientId)
                ?? throw new InvalidOperationException("Missing YouTube client secret");
            var tokens = await _oauth.AuthorizeAsync(
                Connection.ClientId,
                clientSecret,
                uri => _ = Launcher.LaunchUriAsync(uri),
                YouTubeSyncService.Scopes);
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
            {
                _vault.SetYouTubeRefreshToken(Connection.ClientId, tokens.RefreshToken);
            }

            await LoadAsync();
            StatusMessage = "Authorized. Press Sync to fetch YouTube data.";
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
        await EnsureConfiguredFromGoogleDefaultsAsync();
        if (Connection is null || string.IsNullOrWhiteSpace(Connection.ClientId))
        {
            StatusMessage = "Configure Google OAuth credentials first";
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing YouTube...";
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
        await EnsureConfiguredFromGoogleDefaultsAsync();
        if (Connection is null || !Connection.HasRefreshToken)
        {
            StatusMessage = "Connect YouTube account first";
            return;
        }

        IsBusy = true;
        StatusMessage = "Syncing YouTube...";
        try
        {
            var result = await _sync.SyncAsync(Connection.ClientId, SyncDays);
            StatusMessage = $"Synced {result.DailyRows} day(s), {result.VideoRows} video row(s), {result.RetentionRows} retention point(s)";
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
        if (Connection is { ClientId.Length: > 0 })
        {
            _vault.DeleteYouTubeClientSecret(Connection.ClientId);
            _vault.DeleteYouTubeRefreshToken(Connection.ClientId);
        }

        await _connectionsRepo.DeleteYouTubeAsync();
        await LoadAsync();
        NewClientSecret = "";
        StatusMessage = "Removed";
    }

    private async Task EnsureConfiguredFromGoogleDefaultsAsync()
    {
        if (Connection is { ClientId.Length: > 0 })
        {
            return;
        }

        var googleConnection = await _connectionsRepo.GetSearchConsoleAsync();
        if (string.IsNullOrWhiteSpace(googleConnection?.ClientId))
        {
            return;
        }

        await _connectionsRepo.UpsertYouTubeAsync(
            new YouTubeConnectionConfig
            {
                ClientId = googleConnection.ClientId,
                ChannelId = "",
                ChannelTitle = "",
                LastValidatedAt = _connectionsRepo.FormatNow(),
            },
            string.IsNullOrWhiteSpace(ResolveClientSecret(googleConnection.ClientId)) ? "disconnected" : "configured");
        await LoadAsync();
    }

    private string? ResolveClientSecret(string clientId)
        => _vault.GetYouTubeClientSecret(clientId) ?? _vault.GetSearchConsoleClientSecret(clientId);
}
