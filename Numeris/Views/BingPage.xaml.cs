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

public sealed partial class BingPage : Page
{
    public BingViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _trafficChart;
    private bool _wiringUi;

    public BingPage()
    {
        ViewModel = App.Services.GetRequiredService<BingViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildChart(ref _trafficChart, TrafficChartHost);

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
        ApplyChartData();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BingViewModel.TrafficSeries)
            or nameof(BingViewModel.TrafficXAxes)
            or nameof(BingViewModel.TrafficYAxes))
        {
            ApplyChartData();
        }
    }

    private static void BuildChart(ref CartesianChart? chart, Border host)
    {
        if (chart is not null) return;
        chart = ChartTheme.CreateCartesianChart();
        host.Child = ChartTheme.CreateChartSurface(chart);
    }

    private void ApplyChartData()
    {
        if (_trafficChart is null) return;
        _trafficChart.Series = ViewModel.TrafficSeries;
        _trafficChart.XAxes = ViewModel.TrafficXAxes;
        _trafficChart.YAxes = ViewModel.TrafficYAxes;
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item) return;
        if (item.Tag is not string tag) return;
        OverviewPanel.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        QueriesPanel.Visibility = tag == "queries" ? Visibility.Visible : Visibility.Collapsed;
        PagesPanel.Visibility = tag == "pages" ? Visibility.Visible : Visibility.Collapsed;
        CrawlPanel.Visibility = tag == "crawl" ? Visibility.Visible : Visibility.Collapsed;
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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }
}
