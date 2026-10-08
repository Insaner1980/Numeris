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
using Numeris.Controls;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public sealed partial class SearchConsoleViewModel : ObservableObject, IDisposable
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly ShellViewModel _shell;
    private int _loadVersion;
    private bool _disposed;
    private readonly SearchConsoleRepository _scRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly SearchConsoleSyncService _sync;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial string RefreshStatusMessage { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity RefreshSeverity { get; set; } = InfoBarSeverity.Informational;
    public bool CanRefresh => !IsLoading && !IsRefreshing && !IsInspectingIndexing;
    public bool HasRefreshStatusMessage => !string.IsNullOrWhiteSpace(RefreshStatusMessage);
    [ObservableProperty] public partial string ActiveTab { get; set; } = "overview";
    [ObservableProperty] public partial string QuerySortBy { get; set; } = "clicks";
    [ObservableProperty] public partial string QueryFilter { get; set; } = "";
    // Overview
    [ObservableProperty] public partial ISeries[] OverviewSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] OverviewXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] OverviewYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial string TotalClicksText { get; set; } = "—";
    [ObservableProperty] public partial string TotalImpressionsText { get; set; } = "—";
    [ObservableProperty] public partial string AvgPositionText { get; set; } = "—";
    // Queries
    [ObservableProperty] public partial ObservableCollection<SearchQuery> Queries { get; set; } = new();
    private List<SearchQuery> _allQueries = new();
    [ObservableProperty] public partial ObservableCollection<SearchQuery> NewQueries { get; set; } = new();
    // Pages
    [ObservableProperty] public partial ObservableCollection<SearchPageRow> Pages { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<DecliningPage> DecliningPages { get; set; } = new();
    // Devices
    [ObservableProperty] public partial ISeries[] DevicesSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] DevicesXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] DevicesYAxes { get; set; } = Array.Empty<Axis>();
    // Indexing
    [ObservableProperty] public partial ObservableCollection<SitemapUrl> SitemapUrls { get; set; } = new();
    [ObservableProperty] public partial string IndexingSummaryText { get; set; } = "—";
    [ObservableProperty] public partial string IndexingDetailText { get; set; } = "";
    [ObservableProperty] public partial bool IsInspectingIndexing { get; set; }
    public SearchConsoleViewModel(
        ShellViewModel shell,
        SearchConsoleRepository scRepo,
        SitemapRepository sitemapRepo,
        SearchConsoleSyncService sync)
    {
        _shell = shell;
        _scRepo = scRepo;
        _sitemapRepo = sitemapRepo;
        _sync = sync;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnIsRefreshingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnRefreshStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasRefreshStatusMessage));
    partial void OnIsInspectingIndexingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

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

    partial void OnQuerySortByChanged(string value) => _ = LoadFromShellAsync();
    partial void OnQueryFilterChanged(string value) => ApplyQueryFilter();

    [RelayCommand]
    public async Task InspectIndexingAsync()
    {
        if (IsInspectingIndexing)
        {
            return;
        }

        IsInspectingIndexing = true;
        try
        {
            var domain = _shell.SelectedDomain;
            IndexingDetailText = "Inspecting sitemap URLs with Google Search Console...";
            var urls = await _sitemapRepo.ListUrlsAsync(domain);
            var domains = domain == "all"
                ? urls.Where(url => url.RemovedAt is null).Select(url => url.Domain).Distinct().ToArray()
                : new[] { domain };
            if (domains.Length == 0) throw new InvalidOperationException("No sitemap URLs found. Refresh sitemap first.");
            var result = new IndexingInspectionResult { Domain = domain };
            foreach (var site in domains)
            {
                try
                {
                    var progress = new Progress<IndexingInspectionResult>(p =>
                        IndexingDetailText = $"{site}: checked {p.UrlsChecked + p.Errors} / {p.TotalUrls} URLs");
                    var inspected = await _sync.InspectSitemapUrlsAsync(site, progress);
                    result.UrlsChecked += inspected.UrlsChecked;
                    result.Errors += inspected.Errors;
                    result.FirstError ??= inspected.FirstError;
                }
                catch (Exception ex)
                {
                    result.Errors += urls.Count(url => url.Domain == site && url.RemovedAt is null);
                    result.FirstError ??= ApiErrorMessage.Sanitize(ex);
                }
            }
            BuildIndexing(await _sitemapRepo.ListUrlsAsync(domain));
            if (result.Errors > 0 && result.UrlsChecked == 0)
                IndexingDetailText = $"Inspection failed for all URLs: {result.FirstError ?? "unknown error"}";
            else if (result.Errors > 0)
                IndexingDetailText = $"Checked {result.UrlsChecked} URLs; {result.Errors} failed";
            else
                IndexingDetailText = $"Checked {result.UrlsChecked} URLs";
            if (result.Errors > 0)
            {
                RefreshSeverity = result.UrlsChecked == 0 ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
                RefreshStatusMessage = IndexingDetailText;
            }
        }
        catch (Exception ex)
        {
            IndexingDetailText = $"Inspection failed: {ApiErrorMessage.Sanitize(ex)}";
            RefreshSeverity = InfoBarSeverity.Error;
            RefreshStatusMessage = IndexingDetailText;
        }
        finally
        {
            IsInspectingIndexing = false;
        }
    }

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
            var startStr = range.Start.ToString(DateFormat, CultureInfo.InvariantCulture);
            var endStr = range.End.ToString(DateFormat, CultureInfo.InvariantCulture);
            var prevStart = range.Start.AddDays(-range.Days).ToString(DateFormat, CultureInfo.InvariantCulture);
            var prevEnd = range.Start.AddDays(-1).ToString(DateFormat, CultureInfo.InvariantCulture);
            var domain = _shell.SelectedDomain;

            var dailyTask = _scRepo.GetSearchDailyAsync(domain, startStr, endStr);
            var queriesTask = _scRepo.GetQueriesAsync(domain, startStr, endStr, QuerySortBy, 100);
            var pagesTask = _scRepo.GetPagesAsync(domain, startStr, endStr, 50);
            var devicesTask = _scRepo.GetDevicesAsync(domain, startStr, endStr);
            var newQueriesTask = domain == "all"
                ? Task.FromResult(new List<SearchQuery>())
                : _scRepo.GetNewQueriesAsync(domain, startStr, endStr, prevStart, prevEnd, 20);
            var decliningTask = domain == "all"
                ? Task.FromResult(new List<DecliningPage>())
                : _scRepo.GetDecliningPagesAsync(domain, startStr, endStr, prevStart, prevEnd, 10, 5);
            var sitemapTask = _sitemapRepo.ListUrlsAsync(domain);

            await Task.WhenAll(dailyTask, queriesTask, pagesTask, devicesTask, newQueriesTask, decliningTask, sitemapTask);
            if (_disposed || loadVersion != _loadVersion) return;

            BuildOverview(await dailyTask);
            _allQueries = await queriesTask;
            ApplyQueryFilter();
            ReplaceCollection(NewQueries, await newQueriesTask);
            ReplaceCollection(Pages, await pagesTask);
            ReplaceCollection(DecliningPages, await decliningTask);
            BuildDevicesChart(await devicesTask);
            BuildIndexing(await sitemapTask);
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
        RefreshStatusMessage = "Refreshing Google Search Console data...";
        try
        {
            var result = await _sync.SyncConfiguredAsync(_shell.SelectedPeriod.Days());
            if (result is null)
            {
                RefreshSeverity = InfoBarSeverity.Warning;
                RefreshStatusMessage = "Connect Google Search Console in Sources first.";
                return;
            }
            await LoadAsync();
            RefreshSeverity = InfoBarSeverity.Success;
            RefreshStatusMessage = $"Search Console updated: {result.RecordsUpserted} rows saved.";

            if (ActiveTab == "indexing")
            {
                await InspectIndexingAsync();
            }
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

    private void ApplyQueryFilter()
    {
        var filter = QueryFilter.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allQueries
            : _allQueries.Where(q => q.Query.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        ReplaceCollection(Queries, filtered);
    }

    private void BuildOverview(IReadOnlyList<SearchDay> rows)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        OverviewSeries = new ISeries[]
        {
            CreateLine("Clicks", rows.Select(r => r.TotalClicks).ToArray(), ChartPalette.Accent, fill: true, scalesYAt: 0),
            CreateLine("Impressions", rows.Select(r => r.TotalImpressions).ToArray(), ChartPalette.Secondary, fill: false, scalesYAt: 1),
        };
        OverviewXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        OverviewYAxes = new[]
        {
            ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }),
            ChartTheme.StyleYAxis(new Axis { MinLimit = 0, Position = LiveChartsCore.Measure.AxisPosition.End })
        };

        TotalClicksText = rows.Sum(r => r.TotalClicks).ToString("N0", CultureInfo.CurrentCulture);
        TotalImpressionsText = rows.Sum(r => r.TotalImpressions).ToString("N0", CultureInfo.CurrentCulture);
        var impressions = rows.Sum(r => r.TotalImpressions);
        AvgPositionText = impressions > 0
            ? (rows.Sum(r => r.AvgPosition * r.TotalImpressions) / impressions).ToString("0.0", CultureInfo.CurrentCulture)
            : "—";
    }

    private void BuildDevicesChart(IReadOnlyList<SearchDeviceDay> rows)
    {
        var dates = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToArray();
        var labels = dates.Select(ShortDate).ToArray();
        var devices = rows.Select(r => r.Device).Distinct().OrderBy(d => d).ToArray();

        var series = new List<ISeries>();
        for (var i = 0; i < devices.Length; i++)
        {
            var device = devices[i];
            var dict = rows.Where(r => r.Device == device)
                            .GroupBy(r => r.Date)
                            .ToDictionary(g => g.Key, g => g.Sum(r => r.Clicks));
            var values = dates.Select(d => dict.TryGetValue(d, out var v) ? v : 0L).ToArray();
            series.Add(i == 0
                ? ChartTheme.CreateMatteColumnSeries(device, values)
                : ChartTheme.CreateMutedColumnSeries(device, values));
        }
        DevicesSeries = series.ToArray();
        DevicesXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        DevicesYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
    }

    private void BuildIndexing(IReadOnlyList<SitemapUrl> urls)
    {
        var active = urls.Where(u => u.RemovedAt is null).ToList();
        var inspected = active.Count(u => u.HasInspectionData);
        var indexed = active.Count(u => u.IsIndexed);
        SitemapUrls = new(active);
        IndexingSummaryText = active.Count switch
        {
            0 => "No sitemap data",
            _ when inspected == 0 => $"{active.Count} URLs in sitemap",
            _ => $"{indexed} indexed by URL Inspection",
        };
        IndexingDetailText = active.Count switch
        {
            0 => "Refresh sitemap on Health page",
            _ when inspected == 0 => "Page indexing totals are not available in Numeris yet",
            _ => $"{inspected} inspected sitemap URLs; Google Search Console's Page indexing report can differ because it is a separate aggregate report",
        };
    }

    private static LineSeries<long> CreateLine(string name, long[] values, SKColor color, bool fill, int scalesYAt = 0)
    {
        return new LineSeries<long>
        {
            Name = name,
            Values = values,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            GeometryFill = new SolidColorPaint(color),
            Fill = fill ? new SolidColorPaint(color.WithAlpha(40)) : null,
            GeometrySize = 0,
            LineSmoothness = 0.4,
            ScalesYAt = scalesYAt,
        };
    }

    private static string ShortDate(string isoDate)
    {
        if (DateTime.TryParseExact(isoDate, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return $"{d.Month}/{d.Day}";
        }
        return isoDate;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }
}
