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
    private Period _selectedPeriod = Period.Last7Days;

    [ObservableProperty]
    private string _selectedDomain = "all";

    [ObservableProperty]
    private string _lastPage = "dashboard";

    [ObservableProperty]
    private bool _isRefreshing;

    public string[] AvailableDomains { get; } = { "all", Domains.KnitTools, Domains.Finnvek };

    public ShellViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settings = _settingsStore.Load();
        _selectedPeriod = settings.SelectedPeriod;
        _selectedDomain = AvailableDomains.Contains(settings.SelectedDomain) ? settings.SelectedDomain : "all";
        _lastPage = IsKnownPage(settings.LastPage) ? settings.LastPage : "dashboard";
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
