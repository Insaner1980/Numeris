using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml.Controls;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public sealed partial class PerformanceViewModel : ObservableObject, IDisposable
{
    private const string LargestContentfulPaintMetric = "largest_contentful_paint";

    private readonly ShellViewModel _shell;
    private int _loadVersion;
    private bool _disposed;
    private readonly PerformanceRepository _performanceRepo;
    private readonly PerformanceSyncService _sync;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial string RefreshStatusMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity RefreshSeverity { get; set; } = InfoBarSeverity.Informational;
    public bool CanRefresh => !IsLoading && !IsRefreshing;
    public bool HasRefreshStatusMessage => !string.IsNullOrWhiteSpace(RefreshStatusMessage);
    [ObservableProperty] public partial string ActiveTab { get; set; } = "overview";
    [ObservableProperty] public partial string SelectedMetric { get; set; } = LargestContentfulPaintMetric;
    [ObservableProperty] public partial string SelectedFormFactor { get; set; } = "PHONE";
    [ObservableProperty] public partial string WebVitalsStatus { get; set; } = "No data";
    [ObservableProperty] public partial string WebVitalsDetail { get; set; } = "No CrUX data";
    [ObservableProperty] public partial string MobileScoreText { get; set; } = "—";
    [ObservableProperty] public partial string DesktopScoreText { get; set; } = "—";
    [ObservableProperty] public partial ObservableCollection<CruxMetricSummary> CoreVitals { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<PageSpeedLatestRun> LatestRuns { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<PageSpeedAuditIssue> AuditIssues { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<PerformanceUrlInfo> Urls { get; set; } = new();
    [ObservableProperty] public partial ISeries[] CruxSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] CruxXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] CruxYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial ISeries[] PageSpeedSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] PageSpeedXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] PageSpeedYAxes { get; set; } = Array.Empty<Axis>();

    public string[] MetricOptions { get; } =
    [
        LargestContentfulPaintMetric,
        "interaction_to_next_paint",
        "cumulative_layout_shift",
    ];

    public string[] FormFactorOptions { get; } = ["PHONE", "DESKTOP", "TABLET", "ALL"];

    public PerformanceViewModel(ShellViewModel shell, PerformanceRepository performanceRepo, PerformanceSyncService sync)
    {
        _shell = shell;
        _performanceRepo = performanceRepo;
        _sync = sync;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnIsRefreshingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnRefreshStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasRefreshStatusMessage));

    public void Dispose()
    {
        _disposed = true;
        _loadVersion++;
        _shell.PropertyChanged -= OnShellChanged;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedPeriod) or nameof(ShellViewModel.SelectedDomain))
        {
            _ = LoadFromShellAsync();
        }
    }

    partial void OnSelectedMetricChanged(string value) => _ = LoadFromShellAsync();
    partial void OnSelectedFormFactorChanged(string value) => _ = LoadFromShellAsync();

    private async Task LoadFromShellAsync()
    {
        var loadVersion = _loadVersion + 1;
        try { await LoadAsync(); }
        catch (Exception ex)
        {
            if (_disposed || loadVersion != _loadVersion) return;
            RefreshSeverity = InfoBarSeverity.Error;
            RefreshStatusMessage = $"Load failed: {ApiErrorMessage.Sanitize(ex)}. Previously loaded data is still shown.";
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_disposed) return;
        var loadVersion = ++_loadVersion;
        IsLoading = true;
        try
        {
            var domain = _shell.SelectedDomain;
            var range = _shell.SelectedPeriod.ToDateRange();
            var start = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var pageSpeedStart = range.Start.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var pageSpeedEnd = range.End.ToDateTime(TimeOnly.MaxValue).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

            var vitalsTask = _performanceRepo.GetLatestCruxCoreVitalsAsync(domain);
            var cruxTrendTask = _performanceRepo.GetCruxTrendAsync(domain, SelectedMetric, SelectedFormFactor, start, end);
            var runsTask = _performanceRepo.GetLatestPageSpeedRunsAsync(domain);
            var scoreTrendTask = _performanceRepo.GetPageSpeedScoreTrendAsync(domain, pageSpeedStart, pageSpeedEnd);
            var issuesTask = _performanceRepo.GetPageSpeedAuditIssuesAsync(domain, 50);
            var urlsTask = _performanceRepo.ListUrlsAsync();

            await Task.WhenAll(vitalsTask, cruxTrendTask, runsTask, scoreTrendTask, issuesTask, urlsTask);
            if (_disposed || loadVersion != _loadVersion) return;

            var vitals = await vitalsTask;
            ReplaceCollection(CoreVitals, vitals);
            BuildVitalsSummary(vitals);
            BuildCruxTrend(await cruxTrendTask);

            var runs = await runsTask;
            ReplaceCollection(LatestRuns, runs);
            BuildPageSpeedSummary(runs);
            BuildPageSpeedTrend(await scoreTrendTask);
            ReplaceCollection(AuditIssues, await issuesTask);
            ReplaceCollection(Urls, await urlsTask);
        }
        finally
        {
            if (!_disposed && loadVersion == _loadVersion) IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!CanRefresh)
        {
            return;
        }

        IsRefreshing = true;
        RefreshSeverity = InfoBarSeverity.Informational;
        RefreshStatusMessage = "Refreshing CrUX and PageSpeed data for all configured URLs...";
        try
        {
            var result = await _sync.SyncConfiguredAsync();
            if (result is null)
            {
                RefreshSeverity = InfoBarSeverity.Warning;
                RefreshStatusMessage = "Configure CrUX or PageSpeed in Sources first.";
                return;
            }
            await LoadAsync();
            RefreshSeverity = result.UrlsSynced == 0 || result.PageSpeedErrors > 0 || result.CruxSkipped > 0
                || (result.CruxMetricPoints == 0 && result.PageSpeedRuns == 0)
                ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            var missingFieldData = result.CruxSkipped > 0 ? $" {result.CruxSkipped} CrUX target/form-factor request(s) had no field data." : "";
            var pageSpeedErrors = result.PageSpeedErrors > 0 ? $" {result.PageSpeedErrors} PageSpeed request(s) failed." : "";
            RefreshStatusMessage = result.UrlsSynced == 0
                ? "No active performance URLs. Add a URL in Sources first."
                : $"Processed {result.UrlsSynced} configured URL(s): stored {result.CruxMetricPoints} CrUX metric points and {result.PageSpeedRuns} PageSpeed reports."
                    + missingFieldData + pageSpeedErrors;
        }
        catch (Exception ex)
        {
            RefreshSeverity = InfoBarSeverity.Error;
            RefreshStatusMessage = $"Refresh failed: {ApiErrorMessage.Sanitize(ex)}. Previously loaded data is still shown.";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void BuildVitalsSummary(List<CruxMetricSummary> vitals)
    {
        if (vitals.Count == 0)
        {
            WebVitalsStatus = "No data";
            WebVitalsDetail = "No CrUX data";
            return;
        }

        WebVitalsStatus = "No data";
        if (vitals.Any(v => v.Status == "Fail")) WebVitalsStatus = "Fail";
        else if (vitals.Any(v => v.Status == "Warn")) WebVitalsStatus = "Warn";
        else if (vitals.Any(v => v.Status == "Pass")) WebVitalsStatus = "Pass";
        WebVitalsDetail = $"{vitals.Count(v => v.Metric == LargestContentfulPaintMetric)} LCP, {vitals.Count(v => v.Metric == "interaction_to_next_paint")} INP, {vitals.Count(v => v.Metric == "cumulative_layout_shift")} CLS";
    }

    private void BuildPageSpeedSummary(IReadOnlyList<PageSpeedLatestRun> runs)
    {
        MobileScoreText = ScoreText(runs.Where(r => r.Strategy == "MOBILE").Select(r => r.PerformanceScore));
        DesktopScoreText = ScoreText(runs.Where(r => r.Strategy == "DESKTOP").Select(r => r.PerformanceScore));
    }

    private void BuildCruxTrend(IReadOnlyList<CruxTrendPoint> rows)
    {
        CruxSeries = new ISeries[]
        {
            new LineSeries<double?>
            {
                Name = MetricLabel(SelectedMetric),
                Values = rows.Select(r => r.P75).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Accent),
                Fill = new SolidColorPaint(ChartPalette.Accent.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
        };
        CruxXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = rows.Select(r => ShortDate(r.CollectionEnd)).ToArray() }) };
        CruxYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
    }

    private void BuildPageSpeedTrend(IReadOnlyList<PageSpeedScorePoint> rows)
    {
        var labels = rows.Select(r => r.AnalysisUtc).Distinct().OrderBy(v => v).Select(ShortDateTime).ToArray();
        var dates = rows.Select(r => r.AnalysisUtc).Distinct().OrderBy(v => v).ToArray();
        PageSpeedSeries = new ISeries[]
        {
            ChartTheme.CreateMatteColumnSeries("Mobile", ScoreValues(rows, dates, "MOBILE")),
            ChartTheme.CreateMutedColumnSeries("Desktop", ScoreValues(rows, dates, "DESKTOP")),
        };
        PageSpeedXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        PageSpeedYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0, MaxLimit = 100 }) };
    }

    private static double?[] ScoreValues(IReadOnlyList<PageSpeedScorePoint> rows, string[] dates, string strategy)
        => dates
            .Select(date => rows.FirstOrDefault(r => r.AnalysisUtc == date && r.Strategy == strategy)?.PerformanceScore * 100.0)
            .ToArray();

    private static string ScoreText(IEnumerable<double?> scores)
    {
        var values = scores.Where(score => score.HasValue).Select(score => score!.Value * 100.0).ToArray();
        if (values.Length == 0) return "—";
        var min = values.Min().ToString("0", CultureInfo.InvariantCulture);
        var max = values.Max().ToString("0", CultureInfo.InvariantCulture);
        return min == max ? min : $"{min}–{max}";
    }

    private static string MetricLabel(string metric)
        => metric switch
        {
            LargestContentfulPaintMetric => "LCP p75 (ms)",
            "interaction_to_next_paint" => "INP p75 (ms)",
            "cumulative_layout_shift" => "CLS p75 (unitless)",
            _ => metric,
        };

    private static string ShortDate(string isoDate)
    {
        if (DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return $"{d.Month}/{d.Day}";
        }
        return isoDate;
    }

    private static string ShortDateTime(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
        {
            return $"{d.Month}/{d.Day}";
        }
        return value;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }
}
