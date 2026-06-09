using System.ComponentModel;
using System;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Themes;
using Numeris.ViewModels;

namespace Numeris.Views;

public sealed partial class GoogleAnalyticsPage : Page
{
    public GoogleAnalyticsViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _overviewChart;
    private CartesianChart? _deviceChart;
    private bool _wiringUi;

    public GoogleAnalyticsPage()
    {
        ViewModel = App.Services.GetRequiredService<GoogleAnalyticsViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildChart(ref _overviewChart, OverviewChartHost);
        BuildBarChart(ref _deviceChart, DevicesPanel);

        _wiringUi = true;
        DomainCombo.ItemsSource = Shell.AvailableDomains;
        DomainCombo.SelectedItem = Shell.SelectedDomain;
        PeriodSelector.SelectedPeriod = Shell.SelectedPeriod;
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
            case nameof(GoogleAnalyticsViewModel.OverviewSeries):
            case nameof(GoogleAnalyticsViewModel.OverviewXAxes):
            case nameof(GoogleAnalyticsViewModel.OverviewYAxes):
                ApplyChartData(_overviewChart, ViewModel.OverviewSeries, ViewModel.OverviewXAxes, ViewModel.OverviewYAxes);
                break;
            case nameof(GoogleAnalyticsViewModel.DeviceSeries):
            case nameof(GoogleAnalyticsViewModel.DeviceXAxes):
            case nameof(GoogleAnalyticsViewModel.DeviceYAxes):
                ApplyChartData(_deviceChart, ViewModel.DeviceSeries, ViewModel.DeviceXAxes, ViewModel.DeviceYAxes);
                break;
        }
    }

    private static void BuildChart(ref CartesianChart? chart, Border host)
    {
        if (chart is not null) return;
        chart = ChartTheme.CreateCartesianChart();
        host.Child = ChartTheme.CreateChartSurface(chart);
    }

    private static void BuildBarChart(ref CartesianChart? chart, Border host)
    {
        if (chart is not null) return;
        chart = ChartTheme.CreateCartesianChart();
        host.Child = ChartTheme.CreateBarChartSurface(chart);
    }

    private void ApplyAllChartData()
    {
        ApplyChartData(_overviewChart, ViewModel.OverviewSeries, ViewModel.OverviewXAxes, ViewModel.OverviewYAxes);
        ApplyChartData(_deviceChart, ViewModel.DeviceSeries, ViewModel.DeviceXAxes, ViewModel.DeviceYAxes);
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

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item) return;
        if (item.Tag is not string tag) return;
        OverviewPanel.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        PagesPanel.Visibility = tag == "pages" ? Visibility.Visible : Visibility.Collapsed;
        AcquisitionPanel.Visibility = tag == "acquisition" ? Visibility.Visible : Visibility.Collapsed;
        EventsPanel.Visibility = tag == "events" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = tag == "devices" ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.ActiveTab = tag;
    }

    private void DomainCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (DomainCombo.SelectedItem is string s) Shell.SelectedDomain = s;
    }

    private void PeriodSelector_SelectionChanged(object? sender, EventArgs e)
    {
        if (_wiringUi) return;
        Shell.SelectedPeriod = PeriodSelector.SelectedPeriod;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }
}
