using System;
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
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private static readonly SKColor AccentGold = SKColor.Parse("#D9A24E");
    private static readonly SKColor SecondaryGray = new(236, 238, 242, 80);
    private static readonly SKColor Coral = SKColor.Parse("#C97B6A");

    private readonly ShellViewModel _shell;
    private readonly SummaryRepository _summaryRepo;
    private readonly CloudflareRepository _cfRepo;
    private readonly SearchConsoleRepository _scRepo;
    private readonly SitemapRepository _sitemapRepo;

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial string VisitorsTotal { get; set; } = "—";
    [ObservableProperty] public partial double? VisitorsChangePct { get; set; }
    [ObservableProperty] public partial string ClicksTotal { get; set; } = "—";
    [ObservableProperty] public partial double? ClicksChangePct { get; set; }
    [ObservableProperty] public partial string AvgPosition { get; set; } = "—";
    [ObservableProperty] public partial string ThreatsTotal { get; set; } = "—";
    [ObservableProperty] public partial string CacheHitRatio { get; set; } = "—";
    [ObservableProperty] public partial string IndexedRatio { get; set; } = "—";
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
        SitemapRepository sitemapRepo)
    {
        _shell = shell;
        _summaryRepo = summaryRepo;
        _cfRepo = cfRepo;
        _scRepo = scRepo;
        _sitemapRepo = sitemapRepo;
        _shell.PropertyChanged += OnShellChanged;
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

            var summaryTask = _summaryRepo.GetSummaryAsync(range.Days);
            var trafficTask = _cfRepo.GetTrafficDailyAsync(domain, startStr, endStr);
            var searchTask = _scRepo.GetSearchDailyAsync(domain, startStr, endStr);
            var cacheTask = _cfRepo.GetCacheDailyAsync(domain, startStr, endStr);
            var securityTask = _cfRepo.GetSecurityDailyAsync(domain, startStr, endStr);
            var sitemapTask = _sitemapRepo.ListUrlsAsync(sitemapDomain);

            await Task.WhenAll(summaryTask, trafficTask, searchTask, cacheTask, securityTask, sitemapTask);

            var summary = await summaryTask;
            var traffic = await trafficTask;
            var search = await searchTask;
            var cache = await cacheTask;
            var security = await securityTask;
            var sitemap = await sitemapTask;

            VisitorsTotal = FormatNumber(summary.VisitorsTotal);
            VisitorsChangePct = summary.VisitorsChangePct;
            ClicksTotal = FormatNumber(summary.ClicksTotal);
            ClicksChangePct = summary.ClicksChangePct;

            var avgPos = search.Count > 0 ? search.Average(d => d.AvgPosition) : 0.0;
            AvgPosition = avgPos.ToString("0.0", CultureInfo.CurrentCulture);

            var totalThreats = security.Sum(d => d.Threats);
            ThreatsTotal = totalThreats.ToString("N0", CultureInfo.CurrentCulture);

            var totalReq = cache.Sum(d => d.TotalRequests);
            var cachedReq = cache.Sum(d => d.CachedRequests);
            CacheHitRatio = totalReq > 0
                ? $"{(double)cachedReq / totalReq * 100.0:0.0}%"
                : "—";

            var activeUrls = sitemap.Where(u => u.RemovedAt is null).ToList();
            var passUrls = activeUrls.Count(u => u.Verdict == "PASS");
            IndexedRatio = activeUrls.Count > 0
                ? $"{passUrls} / {activeUrls.Count}"
                : "—";

            BuildTrafficChart(traffic);
            BuildSearchChart(search);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildTrafficChart(IReadOnlyList<TrafficDay> rows)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        TrafficSeries = new ISeries[]
        {
            new LineSeries<long>
            {
                Name = "Visitors",
                Values = rows.Select(r => r.UniqueVisitors).ToArray(),
                Stroke = new SolidColorPaint(AccentGold) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(AccentGold) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(AccentGold),
                Fill = new SolidColorPaint(AccentGold.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
            new LineSeries<long>
            {
                Name = "Pageviews",
                Values = rows.Select(r => r.Pageviews).ToArray(),
                Stroke = new SolidColorPaint(SecondaryGray) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(SecondaryGray) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(SecondaryGray),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
        };
        TrafficXAxes = new[] { new Axis { Labels = labels, LabelsRotation = 0 } };
        TrafficYAxes = new[] { new Axis { MinLimit = 0 } };
    }

    private void BuildSearchChart(IReadOnlyList<SearchDay> rows)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        SearchSeries = new ISeries[]
        {
            new LineSeries<long>
            {
                Name = "Clicks",
                Values = rows.Select(r => r.TotalClicks).ToArray(),
                Stroke = new SolidColorPaint(AccentGold) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(AccentGold) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(AccentGold),
                Fill = new SolidColorPaint(AccentGold.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
                ScalesYAt = 0,
            },
            new LineSeries<long>
            {
                Name = "Impressions",
                Values = rows.Select(r => r.TotalImpressions).ToArray(),
                Stroke = new SolidColorPaint(Coral) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(Coral) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(Coral),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
                ScalesYAt = 1,
            },
        };
        SearchXAxes = new[] { new Axis { Labels = labels, LabelsRotation = 0 } };
        SearchYAxes = new[]
        {
            new Axis { MinLimit = 0 },
            new Axis { MinLimit = 0, Position = LiveChartsCore.Measure.AxisPosition.End }
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

    private static string FormatNumber(long n)
    {
        if (n >= 1000)
        {
            return $"{n / 1000.0:0.#}k";
        }
        return n.ToString("N0", CultureInfo.CurrentCulture);
    }
}
