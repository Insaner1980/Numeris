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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Api;
using Numeris.Services.Insights;
using Numeris.Services.Sync;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private const string NoDataText = "No data";

    private static readonly CultureInfo EnglishCulture = CultureInfo.InvariantCulture;

    private readonly ShellViewModel _shell;
    private int _loadVersion;
    private bool _disposed;
    private readonly SummaryRepository _summaryRepo;
    private readonly CloudflareRepository _cfRepo;
    private readonly SearchConsoleRepository _scRepo;
    private readonly BingRepository _bingRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly InsightMetricsRepository _insightMetricsRepo;
    private readonly CloudflareSyncService _cloudflareSync;
    private readonly SearchConsoleSyncService _searchSync;
    private readonly BingWebmasterSyncService _bingSync;
    private readonly PerformanceSyncService _performanceSync;

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial string RefreshStatusMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity RefreshSeverity { get; set; } = InfoBarSeverity.Informational;
    public bool CanRefresh => !IsLoading && !IsRefreshing;
    public bool HasRefreshStatusMessage => !string.IsNullOrWhiteSpace(RefreshStatusMessage);
    public ObservableCollection<InsightCard> Insights { get; } = new();
    [ObservableProperty] public partial string InsightsSummaryText { get; set; } = "Connect or refresh sources to generate insights.";
    public bool HasInsightRows => Insights.Count > 0;
    [ObservableProperty] public partial string VisitorsTotal { get; set; } = "—";
    [ObservableProperty] public partial double? VisitorsChangePct { get; set; }
    public double VisitorsChangeValue => VisitorsChangePct ?? 0;
    public Visibility VisitorsChangeVisibility => VisitorsChangePct.HasValue ? Visibility.Visible : Visibility.Collapsed;
    [ObservableProperty] public partial string VisitorsDetail { get; set; } = "";
    [ObservableProperty] public partial string ClicksTotal { get; set; } = "—";
    [ObservableProperty] public partial double? ClicksChangePct { get; set; }
    [ObservableProperty] public partial string ClicksDetail { get; set; } = "";
    [ObservableProperty] public partial string BingClicksTotal { get; set; } = "—";
    [ObservableProperty] public partial double? BingClicksChangePct { get; set; }
    [ObservableProperty] public partial string BingClicksDetail { get; set; } = "";
    [ObservableProperty] public partial string WebVitalsStatus { get; set; } = NoDataText;
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
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public DashboardViewModel(
        ShellViewModel shell,
        SummaryRepository summaryRepo,
        CloudflareRepository cfRepo,
        SearchConsoleRepository scRepo,
        BingRepository bingRepo,
        SitemapRepository sitemapRepo,
        InsightMetricsRepository insightMetricsRepo,
        CloudflareSyncService cloudflareSync,
        SearchConsoleSyncService searchSync,
        BingWebmasterSyncService bingSync,
        PerformanceSyncService performanceSync)
    {
        _shell = shell;
        _summaryRepo = summaryRepo;
        _cfRepo = cfRepo;
        _scRepo = scRepo;
        _bingRepo = bingRepo;
        _sitemapRepo = sitemapRepo;
        _insightMetricsRepo = insightMetricsRepo;
        _cloudflareSync = cloudflareSync;
        _searchSync = searchSync;
        _bingSync = bingSync;
        _performanceSync = performanceSync;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnIsRefreshingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnRefreshStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasRefreshStatusMessage));

    partial void OnVisitorsChangePctChanged(double? value)
    {
        OnPropertyChanged(nameof(VisitorsChangeValue));
        OnPropertyChanged(nameof(VisitorsChangeVisibility));
    }

    public void Dispose()
    {
        _disposed = true;
        _loadVersion++;
        _shell.PropertyChanged -= OnShellChanged;
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedDomain)) OnPropertyChanged(nameof(IndexingScopeLabel));
        if (e.PropertyName is nameof(ShellViewModel.SelectedPeriod) or nameof(ShellViewModel.SelectedDomain))
        {
            _ = LoadFromShellAsync();
        }
    }

    public string IndexingScopeLabel => _shell.SelectedDomain == "all" ? $"Indexed · {Domains.KnitTools}" : "Indexed";

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
            if (_disposed || loadVersion != _loadVersion) return;

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

            VisitorsTotal = summary.VisitorsDays > 0 ? FormatNumber(summary.VisitorsTotal) : NoDataText;
            VisitorsChangePct = summary.VisitorsChangePct;
            VisitorsDetail = DataDetail("Cloudflare", summary.VisitorsDays, range.Days, summary.VisitorsLatestDate);
            ClicksTotal = summary.ClicksDays > 0 ? FormatNumber(summary.ClicksTotal) : NoDataText;
            ClicksChangePct = summary.ClicksChangePct;
            ClicksDetail = DataDetail("Google Search", summary.ClicksDays, range.Days, summary.ClicksLatestDate);
            BingClicksTotal = bingSummary.Days > 0 ? FormatNumber(bingSummary.Clicks) : NoDataText;
            BingClicksChangePct = bingSummary.ClicksChangePct;
            BingClicksDetail = DataDetail("Bing", bingSummary.Days, range.Days, bingSummary.LatestDate);
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
            if (!_disposed && loadVersion == _loadVersion) IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!CanRefresh) return;
        IsRefreshing = true;
        RefreshSeverity = InfoBarSeverity.Informational;
        var updated = new List<string>();
        var missing = new List<string>();
        var failures = new List<string>();
        var warnings = new List<string>();
        try
        {
            var domain = _shell.SelectedDomain;
            var days = _shell.SelectedPeriod.Days();
            await SyncSourceAsync("Cloudflare", async () => (await _cloudflareSync.SyncConfiguredAsync(domain, days)).Count > 0);
            await SyncSourceAsync("Google Search", async () => await _searchSync.SyncConfiguredAsync(days) is not null);
            await SyncSourceAsync("Bing", async () => await _bingSync.SyncConfiguredAsync() is not null);
            await RefreshPerformanceAsync(updated, missing, failures, warnings);

            if (updated.Count > 0) await LoadAsync();
            ApplyRefreshResult(updated, missing, failures, warnings);
        }
        catch (Exception ex)
        {
            RefreshSeverity = InfoBarSeverity.Error;
            RefreshStatusMessage = $"Refresh failed: {ApiErrorMessage.Sanitize(ex)}. Stored data is shown.";
        }
        finally
        {
            IsRefreshing = false;
        }

        async Task SyncSourceAsync(string name, Func<Task<bool>> sync)
        {
            RefreshStatusMessage = $"Refreshing {name}...";
            try
            {
                if (await sync()) updated.Add(name);
                else missing.Add(name);
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ApiErrorMessage.Sanitize(ex)}");
            }
        }
    }

    private void ApplyRefreshResult(List<string> updated, List<string> missing, List<string> failures, List<string> warnings)
    {
        if (failures.Count > 0 && updated.Count == 0) RefreshSeverity = InfoBarSeverity.Error;
        else if (failures.Count > 0 || missing.Count > 0 || warnings.Count > 0) RefreshSeverity = InfoBarSeverity.Warning;
        else RefreshSeverity = InfoBarSeverity.Success;
        RefreshStatusMessage = updated.Count > 0 ? $"Updated: {string.Join(", ", updated)}." : "No sources were updated.";
        if (missing.Count > 0) RefreshStatusMessage += $" Configure {string.Join(", ", missing)} in Sources.";
        if (warnings.Count > 0) RefreshStatusMessage += " " + string.Join("; ", warnings) + ". Stored data is shown.";
        if (failures.Count > 0) RefreshStatusMessage += " Refresh failed: " + string.Join("; ", failures) + ". Stored data is shown.";
    }

    private async Task RefreshPerformanceAsync(List<string> updated, List<string> missing, List<string> failures, List<string> warnings)
    {
        RefreshStatusMessage = "Refreshing Performance...";
        try
        {
            var result = await _performanceSync.SyncConfiguredAsync();
            if (result is null)
            {
                missing.Add("Performance");
            }
            else
            {
                if (result.PageSpeedErrors > 0) failures.Add($"Performance: {result.PageSpeedErrors} PageSpeed request(s) failed");
                if (result.CruxSkipped > 0) warnings.Add($"Performance: {result.CruxSkipped} CrUX target/form-factor request(s) had no field data");
                if (result.CruxMetricPoints > 0 || result.PageSpeedRuns > 0) updated.Add("Performance");
                else warnings.Add("Performance: no new reports were stored");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Performance: {ApiErrorMessage.Sanitize(ex)}");
        }
    }

    private static string DataDetail(string source, int storedDays, int requestedDays, string latestDate)
        => storedDays == 0 ? $"No {source} data for this period. Refresh to fetch data."
            : $"{source} · {storedDays}/{requestedDays} days · through {latestDate}";

    private void ApplyInsights(InsightMetrics insightMetrics)
    {
        var rows = InsightEngine.Generate(insightMetrics);
        Insights.Clear();
        foreach (var row in rows)
        {
            Insights.Add(row);
        }

        InsightsSummaryText = rows.Count == 0
            ? InsightEngine.GetEmptyStateText(insightMetrics)
            : "";
        OnPropertyChanged(nameof(HasInsightRows));
    }

    private void BuildTrafficChart(List<TrafficDay> rows)
    {
        if (rows.Count == 0)
        {
            TrafficSeries = Array.Empty<ISeries>();
            TrafficXAxes = Array.Empty<Axis>();
            TrafficYAxes = Array.Empty<Axis>();
            return;
        }
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        var visitors = rows.Select(r => r.UniqueVisitors).ToArray();
        TrafficSeries = new ISeries[]
        {
            ChartTheme.CreateMatteColumnSeries("Visitors", visitors, HighlightIndex(rows)),
        };
        TrafficXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels, LabelsRotation = 0 }) };
        TrafficYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
    }

    private static int? HighlightIndex(List<TrafficDay> rows)
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
            new LineSeries<long?>
            {
                Name = "Google clicks",
                Values = dates.Select(d => google.TryGetValue(d, out var value) ? (long?)value : null).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Accent),
                Fill = new SolidColorPaint(ChartPalette.Accent.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
            new LineSeries<long?>
            {
                Name = "Bing clicks",
                Values = dates.Select(d => bing.TryGetValue(d, out var value) ? (long?)value : null).ToArray(),
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
