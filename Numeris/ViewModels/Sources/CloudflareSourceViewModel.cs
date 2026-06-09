using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;

namespace Numeris.ViewModels.Sources;

public partial class CloudflareSourceViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly CloudflareGraphqlClient _client;
    private readonly CloudflareSyncService _sync;

    [ObservableProperty] public partial ObservableCollection<CloudflareConnectionInfo> Connections { get; set; } = new();
    [ObservableProperty] public partial string NewDomain { get; set; } = "";
    [ObservableProperty] public partial string NewZoneId { get; set; } = "";
    [ObservableProperty] public partial string NewToken { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    public bool CanRun => !IsBusy;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public CloudflareSourceViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        CloudflareGraphqlClient client,
        CloudflareSyncService sync)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _client = client;
        _sync = sync;
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRun));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public async Task LoadAsync()
    {
        var list = await _connectionsRepo.ListCloudflareConnectionsAsync();
        Connections.Clear();
        foreach (var c in list) Connections.Add(c);
    }

    [RelayCommand]
    public async Task TestAsync()
    {
        if (!ValidateInputs(requireToken: true)) return;
        IsBusy = true;
        StatusMessage = "Testing connection...";
        try
        {
            var domain = SiteIdentity.NormalizeDomain(NewDomain);
            await _client.ValidateZoneAsync(NormalizeCloudflareToken(NewToken), NewZoneId.Trim(), domain);
            StatusMessage = $"OK - token validates {domain}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task TestSavedAsync(CloudflareConnectionInfo? info)
    {
        if (info is null) return;
        IsBusy = true;
        StatusMessage = $"Testing {info.Domain}...";
        try
        {
            var result = await _sync.TestDomainAsync(info.Domain, info.ZoneId);
            StatusMessage = result.Ok ? result.Message : $"Failed: {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!ValidateInputs(requireToken: false)) return;
        IsBusy = true;
        StatusMessage = "Saving...";
        try
        {
            var domain = SiteIdentity.NormalizeDomain(NewDomain);
            var zoneId = NewZoneId.Trim();
            var token = NormalizeCloudflareToken(NewToken);
            if (!string.IsNullOrEmpty(token))
            {
                _vault.SetCloudflareToken(domain, token);
            }
            else if (string.IsNullOrEmpty(_vault.GetCloudflareToken(domain)))
            {
                StatusMessage = "Token is required on first save";
                return;
            }

            await _connectionsRepo.UpsertCloudflareAsync(
                new CloudflareConnectionConfig
                {
                    Domain = domain,
                    ZoneId = zoneId,
                    LastValidatedAt = _connectionsRepo.FormatNow(),
                },
                "configured");
            await LoadAsync();

            NewToken = "";
            StatusMessage = $"Saved {domain}. Use Cloudflare page refresh to fetch live data.";
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
    public async Task DeleteAsync(CloudflareConnectionInfo? info)
    {
        if (info is null) return;
        await _connectionsRepo.DeleteCloudflareAsync(info.Domain);
        _vault.DeleteCloudflareToken(info.Domain);
        await LoadAsync();
        StatusMessage = $"Removed {info.Domain}";
    }

    private bool ValidateInputs(bool requireToken)
    {
        if (string.IsNullOrWhiteSpace(NewDomain)) { StatusMessage = "Domain is required"; return false; }
        if (string.IsNullOrWhiteSpace(NewZoneId)) { StatusMessage = "Zone ID is required"; return false; }
        if (requireToken && string.IsNullOrWhiteSpace(NewToken)) { StatusMessage = "API token is required"; return false; }
        return true;
    }

    public static string NormalizeCloudflareToken(string token)
    {
        token = token.Trim();
        const string bearerPrefix = "Bearer ";
        return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? token[bearerPrefix.Length..].Trim()
            : token;
    }
}
