using System.ComponentModel;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Models;
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
        BuildChart(ref _incidentsChart, IncidentsChartHost);

        _wiringUi = true;
        DomainCombo.ItemsSource = Shell.AvailableDomains;
        DomainCombo.SelectedItem = Shell.SelectedDomain;
        PeriodCombo.ItemsSource = new[] { Period.Last7Days, Period.Last30Days, Period.Last90Days, Period.All };
        PeriodCombo.SelectedItem = Shell.SelectedPeriod;
        TabBar.SelectedItem = TabBar.MenuItems[0];
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
        chart = new CartesianChart
        {
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom,
            TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Top,
        };
        host.Child = chart;
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

    private void TabBar_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
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

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (PeriodCombo.SelectedItem is Period p) Shell.SelectedPeriod = p;
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
