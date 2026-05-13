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

public sealed partial class YouTubePage : Page
{
    public YouTubeViewModel ViewModel { get; }
    public ShellViewModel Shell { get; }

    private CartesianChart? _dailyChart;
    private CartesianChart? _retentionChart;
    private bool _wiringUi;

    public YouTubePage()
    {
        ViewModel = App.Services.GetRequiredService<YouTubeViewModel>();
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildChart(ref _dailyChart, DailyChartHost);
        BuildChart(ref _retentionChart, RetentionChartHost);

        _wiringUi = true;
        PeriodCombo.ItemsSource = PeriodOptions.All;
        PeriodCombo.SelectedItem = PeriodOptions.All.FirstOrDefault(option => option.Value == Shell.SelectedPeriod);
        TabBar.SelectedItem = TabBar.Items[0];
        _wiringUi = false;

        ViewModel.PropertyChanged += OnViewModelChanged;
        await ViewModel.LoadAsync();
        RetentionVideoCombo.ItemsSource = ViewModel.Videos;
        RetentionVideoCombo.SelectedItem = ViewModel.SelectedRetentionVideo;
        ApplyChartData();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(YouTubeViewModel.DailySeries)
            or nameof(YouTubeViewModel.DailyXAxes)
            or nameof(YouTubeViewModel.DailyYAxes)
            or nameof(YouTubeViewModel.RetentionSeries)
            or nameof(YouTubeViewModel.RetentionXAxes)
            or nameof(YouTubeViewModel.RetentionYAxes))
        {
            ApplyChartData();
        }
        if (e.PropertyName is nameof(YouTubeViewModel.Videos))
        {
            RetentionVideoCombo.ItemsSource = ViewModel.Videos;
        }
        if (e.PropertyName is nameof(YouTubeViewModel.SelectedRetentionVideo))
        {
            RetentionVideoCombo.SelectedItem = ViewModel.SelectedRetentionVideo;
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
        if (_dailyChart is not null)
        {
            _dailyChart.Series = ViewModel.DailySeries;
            _dailyChart.XAxes = ViewModel.DailyXAxes;
            _dailyChart.YAxes = ViewModel.DailyYAxes;
        }
        if (_retentionChart is not null)
        {
            _retentionChart.Series = ViewModel.RetentionSeries;
            _retentionChart.XAxes = ViewModel.RetentionXAxes;
            _retentionChart.YAxes = ViewModel.RetentionYAxes;
        }
    }

    private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem item) return;
        if (item.Tag is not string tag) return;
        OverviewPanel.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        VideosPanel.Visibility = tag == "videos" ? Visibility.Visible : Visibility.Collapsed;
        GeographyPanel.Visibility = tag == "geography" ? Visibility.Visible : Visibility.Collapsed;
        TrafficPanel.Visibility = tag == "traffic" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = tag == "devices" ? Visibility.Visible : Visibility.Collapsed;
        RetentionPanel.Visibility = tag == "retention" ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.ActiveTab = tag;
    }

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (PeriodCombo.SelectedItem is PeriodOption option) Shell.SelectedPeriod = option.Value;
    }

    private void RetentionVideoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_wiringUi) return;
        if (RetentionVideoCombo.SelectedItem is YouTubeVideoStat video) ViewModel.SelectedRetentionVideo = video;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }
}
