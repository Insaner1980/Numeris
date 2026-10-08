using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Settings;

namespace Numeris.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly ConnectionsRepository _connectionsRepo;

    [ObservableProperty]
    public partial Period SelectedPeriod { get; set; }

    [ObservableProperty]
    public partial string SelectedDomain { get; set; }

    [ObservableProperty]
    public partial string LastPage { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial string SettingsErrorMessage { get; set; } = "";

    public bool HasSettingsError => !string.IsNullOrEmpty(SettingsErrorMessage);
    partial void OnSettingsErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasSettingsError));

    public string[] AvailableDomains { get; private set; } = { "all", Domains.KnitTools, Domains.Finnvek };

    public ShellViewModel(SettingsStore settingsStore, ConnectionsRepository connectionsRepo)
    {
        _settingsStore = settingsStore;
        _connectionsRepo = connectionsRepo;
        UpdateAvailableDomains(connectionsRepo.ListConfiguredDomainsAsync().GetAwaiter().GetResult());
        var settings = _settingsStore.Load();
        SelectedPeriod = settings.SelectedPeriod;
        SelectedDomain = AvailableDomains.Contains(settings.SelectedDomain) ? settings.SelectedDomain : "all";
        LastPage = IsKnownPage(settings.LastPage) ? settings.LastPage : "dashboard";
    }

    public async Task RefreshAvailableDomainsAsync()
        => UpdateAvailableDomains(await _connectionsRepo.ListConfiguredDomainsAsync());

    private void UpdateAvailableDomains(IEnumerable<string> domains)
    {
        AvailableDomains = new[] { "all", Domains.KnitTools, Domains.Finnvek }
            .Concat(domains.Where(domain => !string.IsNullOrWhiteSpace(domain)).Select(SiteIdentity.NormalizeDomain))
            .Distinct()
            .ToArray();

        if (SelectedDomain is not null && !AvailableDomains.Contains(SelectedDomain))
        {
            SelectedDomain = "all";
        }
    }

    partial void OnSelectedPeriodChanged(Period value) => SaveSettings();
    partial void OnSelectedDomainChanged(string value) => SaveSettings();
    partial void OnLastPageChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(new ShellSettings
            {
                SelectedPeriod = SelectedPeriod,
                SelectedDomain = AvailableDomains.Contains(SelectedDomain) ? SelectedDomain : "all",
                LastPage = IsKnownPage(LastPage) ? LastPage : "dashboard",
            });
            SettingsErrorMessage = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SettingsErrorMessage = "Preferences could not be saved. Your selection is available for this session.";
        }
    }

    private static bool IsKnownPage(string page)
        => page is "dashboard" or "cloudflare" or "search" or "bing" or "performance" or "health" or "sources";
}
