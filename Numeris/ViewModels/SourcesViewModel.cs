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

namespace Numeris.ViewModels;

public partial class SourcesViewModel : ObservableObject
{
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly CloudflareGraphqlClient _cfClient;
    private readonly CloudflareSyncService _cfSync;

    [ObservableProperty] private ObservableCollection<CloudflareConnectionInfo> _cloudflareConnections = new();

    [ObservableProperty] private string _newCfDomain = "";
    [ObservableProperty] private string _newCfZoneId = "";
    [ObservableProperty] private string _newCfToken = "";
    [ObservableProperty] private string _cfStatusMessage = "";
    [ObservableProperty] private bool _isCfBusy;
    [ObservableProperty] private int _cfSyncDays = 90;

    public SourcesViewModel(
        ConnectionsRepository connectionsRepo,
        CredentialVault vault,
        CloudflareGraphqlClient cfClient,
        CloudflareSyncService cfSync)
    {
        _connectionsRepo = connectionsRepo;
        _vault = vault;
        _cfClient = cfClient;
        _cfSync = cfSync;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var list = await _connectionsRepo.ListCloudflareConnectionsAsync();
        CloudflareConnections.Clear();
        foreach (var c in list) CloudflareConnections.Add(c);
    }

    [RelayCommand]
    public async Task TestCloudflareAsync()
    {
        if (!ValidateCloudflareInputs(requireToken: true)) return;
        IsCfBusy = true;
        CfStatusMessage = "Testing connection...";
        try
        {
            await _cfClient.ValidateZoneAsync(NewCfToken.Trim(), NewCfZoneId.Trim(), NewCfDomain.Trim().ToLowerInvariant());
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
    public async Task SaveCloudflareAsync()
    {
        if (!ValidateCloudflareInputs(requireToken: false)) return;
        IsCfBusy = true;
        CfStatusMessage = "Saving...";
        try
        {
            var domain = NewCfDomain.Trim().ToLowerInvariant();
            var zoneId = NewCfZoneId.Trim();
            var token = NewCfToken.Trim();
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
}
