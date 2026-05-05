using CommunityToolkit.Mvvm.ComponentModel;
using Numeris.Helpers;
using Numeris.Models;

namespace Numeris.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private Period _selectedPeriod = Period.Last7Days;

    [ObservableProperty]
    private string _selectedDomain = "all";

    [ObservableProperty]
    private bool _isRefreshing;

    public string[] AvailableDomains { get; } = { "all", Domains.KnitTools, Domains.Finnvek };
}
