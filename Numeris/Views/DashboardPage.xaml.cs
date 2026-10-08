using System.ComponentModel;
using System;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Themes;
using Numeris.ViewModels;

namespace Numeris.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _trafficChart;
    private CartesianChart? _searchChart;
    private bool _wiringUi;

    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildBarChart(ref _trafficChart, TrafficChartHost);
        BuildChart(ref _searchChart, SearchChartHost);

        await Shell.RefreshAvailableDomainsAsync();
        if (!IsLoaded) return;
        _wiringUi = true;
        DomainCombo.ItemsSource = Shell.AvailableDomains;
        DomainCombo.SelectedItem = Shell.SelectedDomain;
        PeriodSelector.SelectedPeriod = Shell.SelectedPeriod;
        _wiringUi = false;

        ViewModel.PropertyChanged += OnViewModelChanged;
        await ViewModel.LoadAsync();
        if (!IsLoaded) return;
        ApplyChartData();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.Dispose();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DashboardViewModel.TrafficSeries):
            case nameof(DashboardViewModel.TrafficXAxes):
            case nameof(DashboardViewModel.TrafficYAxes):
            case nameof(DashboardViewModel.SearchSeries):
            case nameof(DashboardViewModel.SearchXAxes):
            case nameof(DashboardViewModel.SearchYAxes):
                ApplyChartData();
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

    private void ApplyChartData()
    {
        if (_trafficChart is not null)
        {
            _trafficChart.Series = ViewModel.TrafficSeries;
            _trafficChart.XAxes = ViewModel.TrafficXAxes;
            _trafficChart.YAxes = ViewModel.TrafficYAxes;
        }
        if (_searchChart is not null)
        {
            _searchChart.Series = ViewModel.SearchSeries;
            _searchChart.XAxes = ViewModel.SearchXAxes;
            _searchChart.YAxes = ViewModel.SearchYAxes;
        }
    }

    private void DomainCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (DomainCombo.SelectedItem is string s)
        {
            Shell.SelectedDomain = s;
        }
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
