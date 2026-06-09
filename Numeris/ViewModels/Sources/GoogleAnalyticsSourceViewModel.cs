using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Windows.System;

namespace Numeris.ViewModels.Sources;

public partial class GoogleAnalyticsSourceViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly GoogleOAuthFlow _oauth;
    private readonly GoogleAnalyticsSyncService _sync;

    [ObservableProperty] public partial GoogleAnalyticsConnectionInfo? Connection { get; set; }
    [ObservableProperty] public partial string NewDomain { get; set; } = Domains.KnitTools;
    [ObservableProperty] public partial string NewPropertyId { get; set; } = "";
    [ObservableProperty] public partial string NewClientId { get; set; } = "";
    [ObservableProperty] public partial string NewClientSecret { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    public bool CanRun => !IsBusy;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public GoogleAnalyticsSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        GoogleOAuthFlow oauth,
        GoogleAnalyticsSyncService sync)
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
        Connection = await _connectionsRepo.GetGoogleAnalyticsAsync();
        if (Connection is { PropertyId.Length: > 0 })
        {
            NewDomain = string.IsNullOrWhiteSpace(Connection.Domain) ? Domains.KnitTools : Connection.Domain;
            NewPropertyId = Connection.PropertyId;
            NewClientId = Connection.ClientId;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDomain))
        {
            StatusMessage = "Domain is required";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewPropertyId))
        {
            StatusMessage = "GA4 property ID is required";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewClientId))
        {
            StatusMessage = "Client ID is required";
            return;
        }

        IsBusy = true;
        StatusMessage = "Saving Google Analytics credentials...";
        try
        {
            var domain = SiteIdentity.NormalizeDomain(NewDomain);
            var propertyId = NewPropertyId.Trim();
            var clientId = NewClientId.Trim();
            var secret = NewClientSecret.Trim();
            if (!string.IsNullOrEmpty(secret))
            {
                _vault.SetGoogleAnalyticsClientSecret(clientId, secret);
            }
            else if (string.IsNullOrEmpty(_vault.GetGoogleAnalyticsClientSecret(clientId)))
            {
                StatusMessage = "Client secret is required on first save";
                return;
            }

            await _connectionsRepo.UpsertGoogleAnalyticsAsync(
                new GoogleAnalyticsConnectionConfig
                {
                    Domain = domain,
                    PropertyId = propertyId,
                    ClientId = clientId,
                    LastValidatedAt = _connectionsRepo.FormatNow(),
                },
                "configured");
            NewClientSecret = "";
            await LoadAsync();
            StatusMessage = "Saved. Press Connect Google Analytics to authorize.";
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
        if (Connection is null || string.IsNullOrWhiteSpace(Connection.ClientId) || !Connection.HasClientSecret)
        {
            StatusMessage = "Save Google Analytics credentials first";
            return;
        }

        IsBusy = true;
        StatusMessage = "Opening browser for Google Analytics consent...";
        try
        {
            var clientSecret = _vault.GetGoogleAnalyticsClientSecret(Connection.ClientId)
                ?? throw new InvalidOperationException("Missing Google Analytics client secret");
            var tokens = await _oauth.AuthorizeAsync(
                Connection.ClientId,
                clientSecret,
                uri => _ = Launcher.LaunchUriAsync(uri),
                GoogleAnalyticsSyncService.Scopes);
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
            {
                _vault.SetGoogleAnalyticsRefreshToken(Connection.ClientId, tokens.RefreshToken);
            }

            await LoadAsync();
            StatusMessage = "Authorized. Use Analytics page refresh to fetch GA4 data.";
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
            StatusMessage = "Save Google Analytics credentials first";
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing Google Analytics...";
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
    public async Task DeleteAsync()
    {
        if (Connection is { ClientId.Length: > 0 })
        {
            _vault.DeleteGoogleAnalyticsClientSecret(Connection.ClientId);
            _vault.DeleteGoogleAnalyticsRefreshToken(Connection.ClientId);
        }

        await _connectionsRepo.DeleteGoogleAnalyticsAsync();
        await LoadAsync();
        NewClientSecret = "";
        StatusMessage = "Removed";
    }
}
