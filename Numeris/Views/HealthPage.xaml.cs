using System.ComponentModel;
using System;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Themes;
using Numeris.ViewModels;

namespace Numeris.Views;

public sealed partial class HealthPage : Page
{
    public HealthViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _responseChart;
    private CartesianChart? _incidentsChart;
    private bool _wiringUi;

    public HealthPage()
    {
        ViewModel = App.Services.GetRequiredService<HealthViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildChart(ref _responseChart, ResponseChartHost);
        BuildBarChart(ref _incidentsChart, IncidentsChartHost);

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
            case nameof(HealthViewModel.ResponseSeries):
            case nameof(HealthViewModel.ResponseXAxes):
            case nameof(HealthViewModel.ResponseYAxes):
                if (_responseChart is not null)
                {
                    _responseChart.Series = ViewModel.ResponseSeries;
                    _responseChart.XAxes = ViewModel.ResponseXAxes;
                    _responseChart.YAxes = ViewModel.ResponseYAxes;
                }
                break;
            case nameof(HealthViewModel.IncidentsSeries):
            case nameof(HealthViewModel.IncidentsXAxes):
            case nameof(HealthViewModel.IncidentsYAxes):
                if (_incidentsChart is not null)
                {
                    _incidentsChart.Series = ViewModel.IncidentsSeries;
                    _incidentsChart.XAxes = ViewModel.IncidentsXAxes;
                    _incidentsChart.YAxes = ViewModel.IncidentsYAxes;
                }
                break;
        }
    }

    private static void BuildChart(ref CartesianChart? chart, Border host)
    {
        if (chart is not null) return;
        chart = ChartTheme.CreateCartesianChart();
        chart.LegendPosition = LegendPosition.Hidden;
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
        if (_responseChart is not null)
        {
            _responseChart.Series = ViewModel.ResponseSeries;
            _responseChart.XAxes = ViewModel.ResponseXAxes;
            _responseChart.YAxes = ViewModel.ResponseYAxes;
        }
        if (_incidentsChart is not null)
        {
            _incidentsChart.Series = ViewModel.IncidentsSeries;
            _incidentsChart.XAxes = ViewModel.IncidentsXAxes;
            _incidentsChart.YAxes = ViewModel.IncidentsYAxes;
        }
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item) return;
        if (item.Tag is not string tag) return;
        UptimePanel.Visibility = tag == "uptime" ? Visibility.Visible : Visibility.Collapsed;
        SitemapPanel.Visibility = tag == "sitemap" ? Visibility.Visible : Visibility.Collapsed;
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
        await ViewModel.LoadAsync();
    }

    private async void CheckNowButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CheckUptimeNowAsync();
    }

    private async void RefreshSitemapButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshSitemapAsync();
    }
}
