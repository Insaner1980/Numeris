using System;
using System.Collections.Generic;
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

public partial class CloudflareViewModel : ObservableObject, IDisposable
{
    private static readonly SKColor AccentGold = SKColor.Parse("#D9A24E");
    private static readonly SKColor SecondaryGray = new(236, 238, 242, 80);
    private static readonly SKColor Coral = SKColor.Parse("#C97B6A");
    private static readonly SKColor Success = SKColor.Parse("#84A98C");

    private readonly ShellViewModel _shell;
    private readonly CloudflareRepository _cfRepo;
    private readonly WebAnalyticsRepository _waRepo;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeTab = "traffic";

    // Traffic tab
    [ObservableProperty] private ISeries[] _trafficSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _trafficXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _trafficYAxes = Array.Empty<Axis>();
    [ObservableProperty] private List<BarRow> _trafficCountries = new();
    [ObservableProperty] private List<BarRow> _trafficPages = new();

    // Cache tab
    [ObservableProperty] private ISeries[] _cacheSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _cacheXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _cacheYAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _cacheHitRatioText = "—";
    [ObservableProperty] private string _cacheBytesSavedText = "—";

    // Security tab
    [ObservableProperty] private ISeries[] _securitySeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _securityXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _securityYAxes = Array.Empty<Axis>();
    [ObservableProperty] private string _totalThreatsText = "—";

    // Status codes tab
    [ObservableProperty] private ISeries[] _statusCodesSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _statusCodesXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _statusCodesYAxes = Array.Empty<Axis>();

    // Web Analytics tab
    [ObservableProperty] private ISeries[] _waSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _waXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _waYAxes = Array.Empty<Axis>();
    [ObservableProperty] private List<BarRow> _waReferrers = new();
    [ObservableProperty] private List<BarRow> _waPages = new();
    [ObservableProperty] private List<BarRow> _waCountries = new();

    public CloudflareViewModel(ShellViewModel shell, CloudflareRepository cfRepo, WebAnalyticsRepository waRepo)
    {
        _shell = shell;
        _cfRepo = cfRepo;
        _waRepo = waRepo;
        _shell.PropertyChanged += OnShellChanged;
    }

    public void Dispose()
    {
        _shell.PropertyChanged -= OnShellChanged;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
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

            var trafficTask = _cfRepo.GetTrafficDailyAsync(domain, startStr, endStr);
            var countriesTask = _cfRepo.GetTrafficCountriesAsync(domain, startStr, endStr);
            var pagesTask = _cfRepo.GetTrafficPagesAsync(domain, startStr, endStr);
            var cacheTask = _cfRepo.GetCacheDailyAsync(domain, startStr, endStr);
            var securityTask = _cfRepo.GetSecurityDailyAsync(domain, startStr, endStr);
            var statusTask = _cfRepo.GetStatusCodesDailyAsync(domain, startStr, endStr);
            var waDailyTask = _waRepo.GetDailyAsync(domain, startStr, endStr);
            var waReferrersTask = _waRepo.GetReferrersAsync(domain, startStr, endStr);
            var waPagesTask = _waRepo.GetPagesAsync(domain, startStr, endStr);
            var waCountriesTask = _waRepo.GetCountriesAsync(domain, startStr, endStr);

            await Task.WhenAll(trafficTask, countriesTask, pagesTask, cacheTask, securityTask, statusTask,
                               waDailyTask, waReferrersTask, waPagesTask, waCountriesTask);

            BuildTrafficTab(await trafficTask, await countriesTask, await pagesTask);
            BuildCacheTab(await cacheTask);
            BuildSecurityTab(await securityTask);
            BuildStatusCodesTab(await statusTask);
            BuildWebAnalyticsTab(await waDailyTask, await waReferrersTask, await waPagesTask, await waCountriesTask);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildTrafficTab(IReadOnlyList<TrafficDay> rows, IReadOnlyList<CountryData> countries, IReadOnlyList<PageData> pages)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        TrafficSeries = new ISeries[]
        {
            CreateLine("Visitors", rows.Select(r => r.UniqueVisitors).ToArray(), AccentGold, fill: true),
            CreateLine("Pageviews", rows.Select(r => r.Pageviews).ToArray(), SecondaryGray, fill: false),
        };
        TrafficXAxes = new[] { new Axis { Labels = labels } };
        TrafficYAxes = new[] { new Axis { MinLimit = 0 } };

        TrafficCountries = countries.Select(c => new BarRow { Label = c.Country, Value = c.Value }).ToList();
        TrafficPages = pages.Select(p => new BarRow { Label = p.Path, Value = p.Pageviews }).ToList();
    }

    private void BuildCacheTab(IReadOnlyList<CacheDay> rows)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        var hitPct = rows.Select(r => r.HitRatio * 100.0).ToArray();
        CacheSeries = new ISeries[]
        {
            CreateLine("Hit ratio %", hitPct, AccentGold, fill: true),
        };
        CacheXAxes = new[] { new Axis { Labels = labels } };
        CacheYAxes = new[] { new Axis { MinLimit = 0, MaxLimit = 100 } };

        var totalReq = rows.Sum(r => r.TotalRequests);
        var cachedReq = rows.Sum(r => r.CachedRequests);
        var cachedBytes = rows.Sum(r => r.CachedBytes);
        CacheHitRatioText = totalReq > 0
            ? $"{(double)cachedReq / totalReq * 100.0:0.0}%"
            : "—";
        CacheBytesSavedText = FormatBytes(cachedBytes);
    }

    private void BuildSecurityTab(IReadOnlyList<SecurityDay> rows)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        SecuritySeries = new ISeries[]
        {
            CreateLine("Threats", rows.Select(r => r.Threats).ToArray(), Coral, fill: true),
        };
        SecurityXAxes = new[] { new Axis { Labels = labels } };
        SecurityYAxes = new[] { new Axis { MinLimit = 0 } };
        TotalThreatsText = rows.Sum(r => r.Threats).ToString("N0", CultureInfo.CurrentCulture);
    }

    private void BuildStatusCodesTab(IReadOnlyList<StatusCodeDay> rows)
    {
        var dates = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToArray();
        var labels = dates.Select(ShortDate).ToArray();

        var byCode = rows.GroupBy(r => r.StatusCode).OrderBy(g => g.Key).ToList();
        var palette = new[] { Success, AccentGold, SecondaryGray, Coral };

        var seriesList = new List<ISeries>();
        var i = 0;
        foreach (var group in byCode)
        {
            var dict = group.ToDictionary(r => r.Date, r => r.Requests);
            var values = dates.Select(d => dict.TryGetValue(d, out var v) ? v : 0L).ToArray();
            var color = palette[i % palette.Length];
            seriesList.Add(new StackedColumnSeries<long>
            {
                Name = group.Key.ToString(CultureInfo.InvariantCulture),
                Values = values,
                Fill = new SolidColorPaint(color),
                Stroke = null,
            });
            i++;
        }
        StatusCodesSeries = seriesList.ToArray();
        StatusCodesXAxes = new[] { new Axis { Labels = labels } };
        StatusCodesYAxes = new[] { new Axis { MinLimit = 0 } };
    }

    private void BuildWebAnalyticsTab(
        IReadOnlyList<WebAnalyticsDay> daily,
        IReadOnlyList<WebAnalyticsReferrer> referrers,
        IReadOnlyList<WebAnalyticsPage> pages,
        IReadOnlyList<CountryData> countries)
    {
        var labels = daily.Select(r => ShortDate(r.Date)).ToArray();
        WaSeries = new ISeries[]
        {
            CreateLine("Visits", daily.Select(r => r.Visits).ToArray(), AccentGold, fill: true),
            CreateLine("Page views", daily.Select(r => r.PageViews).ToArray(), SecondaryGray, fill: false),
        };
        WaXAxes = new[] { new Axis { Labels = labels } };
        WaYAxes = new[] { new Axis { MinLimit = 0 } };

        WaReferrers = referrers.Select(r => new BarRow { Label = r.Referrer, Value = r.Visits }).ToList();
        WaPages = pages.Select(p => new BarRow { Label = p.Path, Value = p.PageViews }).ToList();
        WaCountries = countries.Select(c => new BarRow { Label = c.Country, Value = c.Value }).ToList();
    }

    private static LineSeries<long> CreateLine(string name, long[] values, SKColor color, bool fill)
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
        };
    }

    private static LineSeries<double> CreateLine(string name, double[] values, SKColor color, bool fill)
    {
        return new LineSeries<double>
        {
            Name = name,
            Values = values,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            GeometryFill = new SolidColorPaint(color),
            Fill = fill ? new SolidColorPaint(color.WithAlpha(40)) : null,
            GeometrySize = 0,
            LineSmoothness = 0.4,
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

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        const long KB = 1024;
        const long MB = 1024 * KB;
        const long GB = 1024 * MB;
        return bytes switch
        {
            >= GB => $"{(double)bytes / GB:0.00} GB",
            >= MB => $"{(double)bytes / MB:0.0} MB",
            >= KB => $"{(double)bytes / KB:0.0} KB",
            _ => $"{bytes} B"
        };
    }
}
