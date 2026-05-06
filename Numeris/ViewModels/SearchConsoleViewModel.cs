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
using Numeris.Services.Database.Repositories;
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class SearchConsoleViewModel : ObservableObject, IDisposable
{
    private static readonly SKColor AccentGold = SKColor.Parse("#D9A24E");
    private static readonly SKColor SecondaryGray = new(236, 238, 242, 80);
    private static readonly SKColor Coral = SKColor.Parse("#C97B6A");

    private readonly ShellViewModel _shell;
    private readonly SearchConsoleRepository _scRepo;
    private readonly SitemapRepository _sitemapRepo;

    [ObservableProperty] public partial bool IsLoading { get; set; }
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
    public SearchConsoleViewModel(ShellViewModel shell, SearchConsoleRepository scRepo, SitemapRepository sitemapRepo)
    {
        _shell = shell;
        _scRepo = scRepo;
        _sitemapRepo = sitemapRepo;
        _shell.PropertyChanged += OnShellChanged;
    }

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
            CreateLine("Clicks", rows.Select(r => r.TotalClicks).ToArray(), AccentGold, fill: true, scalesYAt: 0),
            CreateLine("Impressions", rows.Select(r => r.TotalImpressions).ToArray(), Coral, fill: false, scalesYAt: 1),
        };
        OverviewXAxes = new[] { new Axis { Labels = labels } };
        OverviewYAxes = new[]
        {
            new Axis { MinLimit = 0 },
            new Axis { MinLimit = 0, Position = LiveChartsCore.Measure.AxisPosition.End }
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
        var palette = new[] { AccentGold, Coral, SecondaryGray };

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
        DevicesXAxes = new[] { new Axis { Labels = labels } };
        DevicesYAxes = new[] { new Axis { MinLimit = 0 } };
    }

    private void BuildIndexing(IReadOnlyList<SitemapUrl> urls)
    {
        var active = urls.Where(u => u.RemovedAt is null).ToList();
        var pass = active.Count(u => u.Verdict == "PASS");
        ReplaceCollection(SitemapUrls, active);
        IndexingSummaryText = active.Count > 0
            ? $"{pass} / {active.Count} indexed"
            : "No sitemap data — refresh on Health page";
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
