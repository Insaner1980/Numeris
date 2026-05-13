using System;
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

public sealed partial class CloudflarePage : Page
{
    public CloudflareViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _trafficChart;
    private CartesianChart? _cacheChart;
    private CartesianChart? _securityChart;
    private CartesianChart? _statusChart;
    private CartesianChart? _waChart;
    private bool _wiringUi;

    public CloudflarePage()
    {
        ViewModel = App.Services.GetRequiredService<CloudflareViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildChart(ref _trafficChart, TrafficChartHost);
        BuildChart(ref _cacheChart, CacheChartHost);
        BuildChart(ref _securityChart, SecurityChartHost);
        BuildChart(ref _statusChart, StatusChartHost);
        BuildChart(ref _waChart, WaChartHost);

        _wiringUi = true;
        DomainCombo.ItemsSource = Shell.AvailableDomains;
        DomainCombo.SelectedItem = Shell.SelectedDomain;
        PeriodCombo.ItemsSource = PeriodOptions.All;
        PeriodCombo.SelectedItem = PeriodOptions.All.FirstOrDefault(option => option.Value == Shell.SelectedPeriod);
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
            case nameof(CloudflareViewModel.TrafficSeries):
            case nameof(CloudflareViewModel.TrafficXAxes):
            case nameof(CloudflareViewModel.TrafficYAxes):
                ApplyChartData(_trafficChart, ViewModel.TrafficSeries, ViewModel.TrafficXAxes, ViewModel.TrafficYAxes);
                break;
            case nameof(CloudflareViewModel.CacheSeries):
            case nameof(CloudflareViewModel.CacheXAxes):
            case nameof(CloudflareViewModel.CacheYAxes):
                ApplyChartData(_cacheChart, ViewModel.CacheSeries, ViewModel.CacheXAxes, ViewModel.CacheYAxes);
                break;
            case nameof(CloudflareViewModel.SecuritySeries):
            case nameof(CloudflareViewModel.SecurityXAxes):
            case nameof(CloudflareViewModel.SecurityYAxes):
                ApplyChartData(_securityChart, ViewModel.SecuritySeries, ViewModel.SecurityXAxes, ViewModel.SecurityYAxes);
                break;
            case nameof(CloudflareViewModel.StatusCodesSeries):
            case nameof(CloudflareViewModel.StatusCodesXAxes):
            case nameof(CloudflareViewModel.StatusCodesYAxes):
                ApplyChartData(_statusChart, ViewModel.StatusCodesSeries, ViewModel.StatusCodesXAxes, ViewModel.StatusCodesYAxes);
                break;
            case nameof(CloudflareViewModel.WaSeries):
            case nameof(CloudflareViewModel.WaXAxes):
            case nameof(CloudflareViewModel.WaYAxes):
                ApplyChartData(_waChart, ViewModel.WaSeries, ViewModel.WaXAxes, ViewModel.WaYAxes);
                break;
        }
    }

    private static void BuildChart(ref CartesianChart? chart, Border host)
    {
        if (chart is not null) return;
        chart = ChartTheme.CreateCartesianChart();
        host.Child = ChartTheme.CreateChartSurface(chart);
    }

    private static void ApplyChartData(
        CartesianChart? chart,
        System.Collections.Generic.IEnumerable<LiveChartsCore.ISeries>? series,
        System.Collections.Generic.IEnumerable<LiveChartsCore.SkiaSharpView.Axis>? xAxes,
        System.Collections.Generic.IEnumerable<LiveChartsCore.SkiaSharpView.Axis>? yAxes)
    {
        if (chart is null) return;
        chart.Series = series;
        chart.XAxes = xAxes;
        chart.YAxes = yAxes;
    }

    private void ApplyAllChartData()
    {
        ApplyChartData(_trafficChart, ViewModel.TrafficSeries, ViewModel.TrafficXAxes, ViewModel.TrafficYAxes);
        ApplyChartData(_cacheChart, ViewModel.CacheSeries, ViewModel.CacheXAxes, ViewModel.CacheYAxes);
        ApplyChartData(_securityChart, ViewModel.SecuritySeries, ViewModel.SecurityXAxes, ViewModel.SecurityYAxes);
        ApplyChartData(_statusChart, ViewModel.StatusCodesSeries, ViewModel.StatusCodesXAxes, ViewModel.StatusCodesYAxes);
        ApplyChartData(_waChart, ViewModel.WaSeries, ViewModel.WaXAxes, ViewModel.WaYAxes);
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item) return;
        if (item.Tag is not string tag) return;

        TrafficPanel.Visibility = tag == "traffic" ? Visibility.Visible : Visibility.Collapsed;
        CachePanel.Visibility = tag == "cache" ? Visibility.Visible : Visibility.Collapsed;
        SecurityPanel.Visibility = tag == "security" ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = tag == "status" ? Visibility.Visible : Visibility.Collapsed;
        WaPanel.Visibility = tag == "webanalytics" ? Visibility.Visible : Visibility.Collapsed;

        ViewModel.ActiveTab = tag;
    }

    private void DomainCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (DomainCombo.SelectedItem is string s)
        {
            Shell.SelectedDomain = s;
        }
    }

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (PeriodCombo.SelectedItem is PeriodOption option)
        {
            Shell.SelectedPeriod = option.Value;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }
}
