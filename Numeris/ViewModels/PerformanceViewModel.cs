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
using Numeris.Models;
using Numeris.Services.Database.Repositories;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class PerformanceViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly PerformanceRepository _performanceRepo;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    public bool CanRefresh => !IsLoading;
    [ObservableProperty] public partial string ActiveTab { get; set; } = "overview";
    [ObservableProperty] public partial string SelectedMetric { get; set; } = "largest_contentful_paint";
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
        "largest_contentful_paint",
        "interaction_to_next_paint",
        "cumulative_layout_shift",
    ];

    public string[] FormFactorOptions { get; } = ["PHONE", "DESKTOP", "TABLET", "ALL"];

    public PerformanceViewModel(ShellViewModel shell, PerformanceRepository performanceRepo)
    {
        _shell = shell;
        _performanceRepo = performanceRepo;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    public void Dispose() => _shell.PropertyChanged -= OnShellChanged;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedPeriod) or nameof(ShellViewModel.SelectedDomain))
        {
            _ = LoadAsync();
        }
    }

    partial void OnSelectedMetricChanged(string value) => _ = ReloadCruxTrendAsync();
    partial void OnSelectedFormFactorChanged(string value) => _ = ReloadCruxTrendAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
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
            IsLoading = false;
        }
    }

    private async Task ReloadCruxTrendAsync()
    {
        var range = _shell.SelectedPeriod.ToDateRange();
        var start = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        BuildCruxTrend(await _performanceRepo.GetCruxTrendAsync(_shell.SelectedDomain, SelectedMetric, SelectedFormFactor, start, end));
    }

    private void BuildVitalsSummary(IReadOnlyList<CruxMetricSummary> vitals)
    {
        if (vitals.Count == 0)
        {
            WebVitalsStatus = "No data";
            WebVitalsDetail = "No CrUX data";
            return;
        }

        WebVitalsStatus = vitals.Any(v => v.Status == "Fail") ? "Fail"
            : vitals.Any(v => v.Status == "Warn") ? "Warn"
            : vitals.Any(v => v.Status == "Pass") ? "Pass"
            : "No data";
        WebVitalsDetail = $"{vitals.Count(v => v.Metric == "largest_contentful_paint")} LCP, {vitals.Count(v => v.Metric == "interaction_to_next_paint")} INP, {vitals.Count(v => v.Metric == "cumulative_layout_shift")} CLS";
    }

    private void BuildPageSpeedSummary(IReadOnlyList<PageSpeedLatestRun> runs)
    {
        MobileScoreText = ScoreText(runs.FirstOrDefault(r => r.Strategy == "MOBILE")?.PerformanceScore);
        DesktopScoreText = ScoreText(runs.FirstOrDefault(r => r.Strategy == "DESKTOP")?.PerformanceScore);
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
            CreateScoreLine("Mobile", rows, dates, "MOBILE", ChartPalette.Accent),
            CreateScoreLine("Desktop", rows, dates, "DESKTOP", ChartPalette.Secondary),
        };
        PageSpeedXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        PageSpeedYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0, MaxLimit = 100 }) };
    }

    private static LineSeries<double?> CreateScoreLine(string name, IReadOnlyList<PageSpeedScorePoint> rows, string[] dates, string strategy, SKColor color)
    {
        var values = dates
            .Select(date => rows.FirstOrDefault(r => r.AnalysisUtc == date && r.Strategy == strategy)?.PerformanceScore * 100.0)
            .ToArray();
        return new LineSeries<double?>
        {
            Name = name,
            Values = values,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            GeometryFill = new SolidColorPaint(color),
            Fill = null,
            GeometrySize = 0,
            LineSmoothness = 0.4,
        };
    }

    private static string ScoreText(double? score)
        => score.HasValue ? (score.Value * 100.0).ToString("0", CultureInfo.InvariantCulture) : "—";

    private static string MetricLabel(string metric)
        => metric switch
        {
            "largest_contentful_paint" => "LCP p75",
            "interaction_to_next_paint" => "INP p75",
            "cumulative_layout_shift" => "CLS p75",
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
