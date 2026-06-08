using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Insights;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.InvariantCulture;

    private readonly ShellViewModel _shell;
    private readonly SummaryRepository _summaryRepo;
    private readonly CloudflareRepository _cfRepo;
    private readonly SearchConsoleRepository _scRepo;
    private readonly BingRepository _bingRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly InsightMetricsRepository _insightMetricsRepo;
    private readonly InsightEngine _insightEngine;

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    public bool CanRefresh => !IsLoading;
    public ObservableCollection<InsightCard> Insights { get; } = new();
    [ObservableProperty] public partial string InsightsSummaryText { get; set; } = "Connect or refresh sources to generate insights.";
    public bool HasInsightRows => Insights.Count > 0;
    [ObservableProperty] public partial string VisitorsTotal { get; set; } = "—";
    [ObservableProperty] public partial double? VisitorsChangePct { get; set; }
    public double VisitorsChangeValue => VisitorsChangePct ?? 0;
    [ObservableProperty] public partial string ClicksTotal { get; set; } = "—";
    [ObservableProperty] public partial double? ClicksChangePct { get; set; }
    [ObservableProperty] public partial string BingClicksTotal { get; set; } = "—";
    [ObservableProperty] public partial double? BingClicksChangePct { get; set; }
    [ObservableProperty] public partial string WebVitalsStatus { get; set; } = "No data";
    [ObservableProperty] public partial string WebVitalsDetail { get; set; } = "No CrUX data";
    [ObservableProperty] public partial string CacheHitRatio { get; set; } = "—";
    [ObservableProperty] public partial string IndexedRatio { get; set; } = "—";
    [ObservableProperty] public partial string PageSpeedMobile { get; set; } = "—";
    [ObservableProperty] public partial string LastSyncText { get; set; } = "Not synced yet";
    [ObservableProperty] public partial ISeries[] TrafficSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] TrafficXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] TrafficYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial ISeries[] SearchSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] SearchXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] SearchYAxes { get; set; } = Array.Empty<Axis>();
    public DashboardViewModel(
        ShellViewModel shell,
        SummaryRepository summaryRepo,
        CloudflareRepository cfRepo,
        SearchConsoleRepository scRepo,
        BingRepository bingRepo,
        SitemapRepository sitemapRepo,
        InsightMetricsRepository insightMetricsRepo,
        InsightEngine insightEngine)
    {
        _shell = shell;
        _summaryRepo = summaryRepo;
        _cfRepo = cfRepo;
        _scRepo = scRepo;
        _bingRepo = bingRepo;
        _sitemapRepo = sitemapRepo;
        _insightMetricsRepo = insightMetricsRepo;
        _insightEngine = insightEngine;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    partial void OnVisitorsChangePctChanged(double? value)
    {
        OnPropertyChanged(nameof(VisitorsChangeValue));
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedPeriod) or nameof(ShellViewModel.SelectedDomain))
        {
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var range = _shell.SelectedPeriod.ToDateRange();
            var startStr = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var endStr = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var domain = _shell.SelectedDomain;
            var sitemapDomain = domain == "all" ? Domains.KnitTools : domain;

            var summaryTask = _summaryRepo.GetSummaryAsync(domain, range.Days);
            var bingSummaryTask = _summaryRepo.GetBingOverviewAsync(domain, range.Days);
            var webVitalsTask = _summaryRepo.GetWebVitalsOverviewAsync(domain);
            var pageSpeedTask = _summaryRepo.GetPageSpeedOverviewAsync(domain);
            var lastSyncTask = _summaryRepo.GetLatestSyncAsync();
            var trafficTask = _cfRepo.GetTrafficDailyAsync(domain, startStr, endStr);
            var searchTask = _scRepo.GetSearchDailyAsync(domain, startStr, endStr);
            var bingTrafficTask = _bingRepo.GetTrafficDailyAsync(domain, startStr, endStr);
            var cacheTask = _cfRepo.GetCacheDailyAsync(domain, startStr, endStr);
            var sitemapTask = _sitemapRepo.ListUrlsAsync(sitemapDomain);
            var insightsTask = _insightMetricsRepo.GetInsightMetricsAsync(domain, range.Days);

            await Task.WhenAll(summaryTask, bingSummaryTask, webVitalsTask, pageSpeedTask, lastSyncTask, trafficTask, searchTask, bingTrafficTask, cacheTask, sitemapTask, insightsTask);

            var summary = await summaryTask;
            var bingSummary = await bingSummaryTask;
            var webVitals = await webVitalsTask;
            var pageSpeed = await pageSpeedTask;
            var traffic = await trafficTask;
            var search = await searchTask;
            var bingTraffic = await bingTrafficTask;
            var cache = await cacheTask;
            var sitemap = await sitemapTask;
            var insightMetrics = await insightsTask;

            VisitorsTotal = FormatNumber(summary.VisitorsTotal);
            VisitorsChangePct = summary.VisitorsChangePct;
            ClicksTotal = FormatNumber(summary.ClicksTotal);
            ClicksChangePct = summary.ClicksChangePct;
            BingClicksTotal = FormatNumber(bingSummary.Clicks);
            BingClicksChangePct = bingSummary.ClicksChangePct;
            WebVitalsStatus = webVitals.Status;
            WebVitalsDetail = webVitals.Detail;
            PageSpeedMobile = pageSpeed.MobilePerformanceScore.HasValue
                ? (pageSpeed.MobilePerformanceScore.Value * 100.0).ToString("0", EnglishCulture)
                : "—";
            LastSyncText = string.IsNullOrWhiteSpace(await lastSyncTask) ? "Not synced yet" : await lastSyncTask;

            var totalReq = cache.Sum(d => d.TotalRequests);
            var cachedReq = cache.Sum(d => d.CachedRequests);
            CacheHitRatio = totalReq > 0
                ? $"{((double)cachedReq / totalReq * 100.0).ToString("0.0", EnglishCulture)}%"
                : "—";

            var activeUrls = sitemap.Where(u => u.RemovedAt is null).ToList();
            var inspectedUrls = activeUrls.Count(u => u.HasInspectionData);
            var indexedUrls = activeUrls.Count(u => u.IsIndexed);
            IndexedRatio = activeUrls.Count switch
            {
                0 => "—",
                _ when inspectedUrls == 0 => "Not inspected",
                _ => $"{indexedUrls} / {inspectedUrls}",
            };

            BuildTrafficChart(traffic);
            BuildSearchChart(search, bingTraffic);
            ApplyInsights(insightMetrics);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyInsights(InsightMetrics insightMetrics)
    {
        var rows = _insightEngine.Generate(insightMetrics);
        Insights.Clear();
        foreach (var row in rows)
        {
            Insights.Add(row);
        }

        InsightsSummaryText = rows.Count == 0
            ? _insightEngine.GetEmptyStateText(insightMetrics)
            : "";
        OnPropertyChanged(nameof(HasInsightRows));
    }

    private void BuildTrafficChart(IReadOnlyList<TrafficDay> rows)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        var visitors = rows.Select(r => r.UniqueVisitors).ToArray();
        TrafficSeries = new ISeries[]
        {
            ChartTheme.CreateMatteColumnSeries("Visitors", visitors, HighlightIndex(rows)),
        };
        TrafficXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels, LabelsRotation = 0 }) };
        TrafficYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
    }

    private static int? HighlightIndex(IReadOnlyList<TrafficDay> rows)
    {
        if (rows.Count == 0 || rows.All(row => row.UniqueVisitors == 0))
        {
            return null;
        }

        if (rows[^1].UniqueVisitors > 0)
        {
            return rows.Count - 1;
        }

        var maxValue = rows.Max(row => row.UniqueVisitors);
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].UniqueVisitors == maxValue)
            {
                return index;
            }
        }

        return null;
    }

    private void BuildSearchChart(IReadOnlyList<SearchDay> googleRows, IReadOnlyList<BingTrafficDay> bingRows)
    {
        var dates = googleRows.Select(r => r.Date)
            .Concat(bingRows.Select(r => r.Date))
            .Distinct()
            .OrderBy(d => d)
            .ToArray();
        var google = googleRows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.TotalClicks));
        var bing = bingRows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Clicks));
        var labels = dates.Select(ShortDate).ToArray();
        SearchSeries = new ISeries[]
        {
            new LineSeries<long>
            {
                Name = "Google clicks",
                Values = dates.Select(d => google.TryGetValue(d, out var value) ? value : 0L).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Accent),
                Fill = new SolidColorPaint(ChartPalette.Accent.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
            new LineSeries<long>
            {
                Name = "Bing clicks",
                Values = dates.Select(d => bing.TryGetValue(d, out var value) ? value : 0L).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Secondary) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Secondary) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Secondary),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
        };
        SearchXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels, LabelsRotation = 0 }) };
        SearchYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
    }

    private static string ShortDate(string isoDate)
    {
        if (DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return $"{d.Month}/{d.Day}";
        }
        return isoDate;
    }

    private static string FormatNumber(long n)
    {
        if (n >= 1000)
        {
            return $"{n / 1000.0:0.#}k";
        }
        return n.ToString("N0", EnglishCulture);
    }
}
