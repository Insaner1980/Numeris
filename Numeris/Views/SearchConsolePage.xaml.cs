using System.ComponentModel;
using System.Linq;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Models;
using Numeris.Themes;
using Numeris.ViewModels;

namespace Numeris.Views;

public sealed partial class SearchConsolePage : Page
{
    public SearchConsoleViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _overviewChart;
    private CartesianChart? _devicesChart;
    private bool _wiringUi;

    public SearchConsolePage()
    {
        ViewModel = App.Services.GetRequiredService<SearchConsoleViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildChart(ref _overviewChart, OverviewChartHost);
        BuildChart(ref _devicesChart, DevicesChartHost);

        _wiringUi = true;
        DomainCombo.ItemsSource = Shell.AvailableDomains;
        DomainCombo.SelectedItem = Shell.SelectedDomain;
        PeriodCombo.ItemsSource = PeriodOptions.All;
        PeriodCombo.SelectedItem = PeriodOptions.All.FirstOrDefault(option => option.Value == Shell.SelectedPeriod);
        QuerySortCombo.ItemsSource = new[] { "clicks", "impressions", "ctr", "position" };
        QuerySortCombo.SelectedItem = ViewModel.QuerySortBy;
        TabBar.SelectedItem = TabBar.Items[0];
        _wiringUi = false;

        ViewModel.PropertyChanged += OnViewModelChanged;
        await ViewModel.LoadAsync();
        ApplyAllChartData();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SearchConsoleViewModel.OverviewSeries):
            case nameof(SearchConsoleViewModel.OverviewXAxes):
            case nameof(SearchConsoleViewModel.OverviewYAxes):
                if (_overviewChart is not null)
                {
                    _overviewChart.Series = ViewModel.OverviewSeries;
                    _overviewChart.XAxes = ViewModel.OverviewXAxes;
                    _overviewChart.YAxes = ViewModel.OverviewYAxes;
                }
                break;
            case nameof(SearchConsoleViewModel.DevicesSeries):
            case nameof(SearchConsoleViewModel.DevicesXAxes):
            case nameof(SearchConsoleViewModel.DevicesYAxes):
                if (_devicesChart is not null)
                {
                    _devicesChart.Series = ViewModel.DevicesSeries;
                    _devicesChart.XAxes = ViewModel.DevicesXAxes;
                    _devicesChart.YAxes = ViewModel.DevicesYAxes;
                }
                break;
        }
    }

    private static void BuildChart(ref CartesianChart? chart, Border host)
    {
        if (chart is not null) return;
        chart = ChartTheme.CreateCartesianChart();
        host.Child = ChartTheme.CreateChartSurface(chart);
    }

    private void ApplyAllChartData()
    {
        if (_overviewChart is not null)
        {
            _overviewChart.Series = ViewModel.OverviewSeries;
            _overviewChart.XAxes = ViewModel.OverviewXAxes;
            _overviewChart.YAxes = ViewModel.OverviewYAxes;
        }
        if (_devicesChart is not null)
        {
            _devicesChart.Series = ViewModel.DevicesSeries;
            _devicesChart.XAxes = ViewModel.DevicesXAxes;
            _devicesChart.YAxes = ViewModel.DevicesYAxes;
        }
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item) return;
        if (item.Tag is not string tag) return;
        OverviewPanel.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        QueriesPanel.Visibility = tag == "queries" ? Visibility.Visible : Visibility.Collapsed;
        PagesPanel.Visibility = tag == "pages" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = tag == "devices" ? Visibility.Visible : Visibility.Collapsed;
        IndexingPanel.Visibility = tag == "indexing" ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.ActiveTab = tag;
    }

    private void DomainCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (DomainCombo.SelectedItem is string s) Shell.SelectedDomain = s;
    }

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (PeriodCombo.SelectedItem is PeriodOption option) Shell.SelectedPeriod = option.Value;
    }

    private void QuerySortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (QuerySortCombo.SelectedItem is string s) ViewModel.QuerySortBy = s;
    }

    private void QueryFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.QueryFilter = QueryFilterBox.Text;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveTab == "indexing")
        {
            await ViewModel.InspectIndexingAsync();
            return;
        }

        await ViewModel.LoadAsync();
    }
}
