using System.ComponentModel;
using System;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Themes;
using Numeris.ViewModels;

namespace Numeris.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _cruxChart;
    private CartesianChart? _pageSpeedChart;
    private bool _wiringUi;

    public PerformancePage()
    {
        ViewModel = App.Services.GetRequiredService<PerformanceViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildChart(ref _cruxChart, CruxChartHost);
        BuildBarChart(ref _pageSpeedChart, PageSpeedChartHost);

        _wiringUi = true;
        DomainCombo.ItemsSource = Shell.AvailableDomains;
        DomainCombo.SelectedItem = Shell.SelectedDomain;
        PeriodSelector.SelectedPeriod = Shell.SelectedPeriod;
        MetricCombo.ItemsSource = ViewModel.MetricOptions;
        MetricCombo.SelectedItem = ViewModel.SelectedMetric;
        FormFactorCombo.ItemsSource = ViewModel.FormFactorOptions;
        FormFactorCombo.SelectedItem = ViewModel.SelectedFormFactor;
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
            case nameof(PerformanceViewModel.CruxSeries):
            case nameof(PerformanceViewModel.CruxXAxes):
            case nameof(PerformanceViewModel.CruxYAxes):
                ApplyChartData(_cruxChart, ViewModel.CruxSeries, ViewModel.CruxXAxes, ViewModel.CruxYAxes);
                break;
            case nameof(PerformanceViewModel.PageSpeedSeries):
            case nameof(PerformanceViewModel.PageSpeedXAxes):
            case nameof(PerformanceViewModel.PageSpeedYAxes):
                ApplyChartData(_pageSpeedChart, ViewModel.PageSpeedSeries, ViewModel.PageSpeedXAxes, ViewModel.PageSpeedYAxes);
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
        ApplyChartData(_cruxChart, ViewModel.CruxSeries, ViewModel.CruxXAxes, ViewModel.CruxYAxes);
        ApplyChartData(_pageSpeedChart, ViewModel.PageSpeedSeries, ViewModel.PageSpeedXAxes, ViewModel.PageSpeedYAxes);
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
        CruxPanel.Visibility = tag == "crux" ? Visibility.Visible : Visibility.Collapsed;
        PageSpeedPanel.Visibility = tag == "pagespeed" ? Visibility.Visible : Visibility.Collapsed;
        UrlsPanel.Visibility = tag == "urls" ? Visibility.Visible : Visibility.Collapsed;
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

    private void MetricCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (MetricCombo.SelectedItem is string s) ViewModel.SelectedMetric = s;
    }

    private void FormFactorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (FormFactorCombo.SelectedItem is string s) ViewModel.SelectedFormFactor = s;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }
}
