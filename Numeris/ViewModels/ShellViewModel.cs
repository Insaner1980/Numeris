using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Settings;

namespace Numeris.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;

    [ObservableProperty]
    public partial Period SelectedPeriod { get; set; }

    [ObservableProperty]
    public partial string SelectedDomain { get; set; }

    [ObservableProperty]
    public partial string LastPage { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public string[] AvailableDomains { get; } = { "all", Domains.KnitTools, Domains.Finnvek };

    public ShellViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settings = _settingsStore.Load();
        SelectedPeriod = settings.SelectedPeriod;
        SelectedDomain = AvailableDomains.Contains(settings.SelectedDomain) ? settings.SelectedDomain : "all";
        LastPage = IsKnownPage(settings.LastPage) ? settings.LastPage : "dashboard";
    }

    partial void OnSelectedPeriodChanged(Period value) => SaveSettings();
    partial void OnSelectedDomainChanged(string value) => SaveSettings();
    partial void OnLastPageChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        _settingsStore.Save(new ShellSettings
        {
            SelectedPeriod = SelectedPeriod,
            SelectedDomain = AvailableDomains.Contains(SelectedDomain) ? SelectedDomain : "all",
            LastPage = IsKnownPage(LastPage) ? LastPage : "dashboard",
        });
    }

    private static bool IsKnownPage(string page)
        => page is "dashboard" or "cloudflare" or "search" or "health" or "sources";
}
