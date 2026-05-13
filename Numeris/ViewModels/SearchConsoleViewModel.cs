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
using Numeris.Controls;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class SearchConsoleViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly SearchConsoleRepository _scRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly SearchConsoleSyncService _sync;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    public bool CanRefresh => !IsLoading && !IsInspectingIndexing;
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
    partial void OnIsInspectingIndexingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    public void Dispose() => _shell.PropertyChanged -= OnShellChanged;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedPeriod) or nameof(ShellViewModel.SelectedDomain))
        {
            _ = LoadAsync();
        }
    }

    partial void OnQuerySortByChanged(string value) => _ = ReloadQueriesAsync();
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
            var domain = _shell.SelectedDomain == "all" ? Domains.KnitTools : _shell.SelectedDomain;
            IndexingDetailText = "Inspecting sitemap URLs with Google Search Console...";
            var progress = new Progress<IndexingInspectionResult>(p =>
            {
                IndexingDetailText = $"Checked {p.UrlsChecked + p.Errors} / {p.TotalUrls} URLs";
            });
            var result = await _sync.InspectSitemapUrlsAsync(domain, progress);
            var urls = await _sitemapRepo.ListUrlsAsync(domain);
            BuildIndexing(urls);
            IndexingDetailText = result.Errors > 0 && result.UrlsChecked == 0
                ? $"Inspection failed for all URLs: {result.FirstError ?? "unknown error"}"
                : result.Errors > 0
                ? $"Checked {result.UrlsChecked} URLs; {result.Errors} failed"
                : $"Checked {result.UrlsChecked} URLs";
        }
        catch (Exception ex)
        {
            IndexingDetailText = $"Inspection failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsInspectingIndexing = false;
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
            var prevStart = range.Start.AddDays(-range.Days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var prevEnd = range.Start.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var domain = _shell.SelectedDomain;
            var sitemapDomain = domain == "all" ? Domains.KnitTools : domain;

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
            var sitemapTask = _sitemapRepo.ListUrlsAsync(sitemapDomain);

            await Task.WhenAll(dailyTask, queriesTask, pagesTask, devicesTask, newQueriesTask, decliningTask, sitemapTask);

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
            IsLoading = false;
        }
    }

    private async Task ReloadQueriesAsync()
    {
        var range = _shell.SelectedPeriod.ToDateRange();
        var startStr = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endStr = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _allQueries = await _scRepo.GetQueriesAsync(_shell.SelectedDomain, startStr, endStr, QuerySortBy, 100);
        ApplyQueryFilter();
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
        var avg = rows.Count > 0 ? rows.Average(r => r.AvgPosition) : 0.0;
        AvgPositionText = avg.ToString("0.0", CultureInfo.CurrentCulture);
    }

    private void BuildDevicesChart(IReadOnlyList<SearchDeviceDay> rows)
    {
        var dates = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToArray();
        var labels = dates.Select(ShortDate).ToArray();
        var devices = rows.Select(r => r.Device).Distinct().OrderBy(d => d).ToArray();
        var palette = new[] { ChartPalette.Accent, ChartPalette.Secondary, ChartPalette.Muted };

        var series = new List<ISeries>();
        for (var i = 0; i < devices.Length; i++)
        {
            var dev = devices[i];
            var dict = rows.Where(r => r.Device == dev)
                            .GroupBy(r => r.Date)
                            .ToDictionary(g => g.Key, g => g.Sum(r => r.Clicks));
            var values = dates.Select(d => dict.TryGetValue(d, out var v) ? v : 0L).ToArray();
            series.Add(CreateLine(dev, values, palette[i % palette.Length], fill: false));
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
        ReplaceCollection(SitemapUrls, active);
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
        if (DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
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
