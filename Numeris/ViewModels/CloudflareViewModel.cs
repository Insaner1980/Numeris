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
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class CloudflareViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly CloudflareRepository _cfRepo;
    private readonly WebAnalyticsRepository _waRepo;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    public bool CanRefresh => !IsLoading;
    [ObservableProperty] public partial string ActiveTab { get; set; } = "traffic";
    // Traffic tab
    [ObservableProperty] public partial ISeries[] TrafficSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] TrafficXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] TrafficYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial List<BarRow> TrafficCountries { get; set; } = new();
    [ObservableProperty] public partial List<BarRow> TrafficPages { get; set; } = new();
    // Cache tab
    [ObservableProperty] public partial ISeries[] CacheSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] CacheXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] CacheYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial string CacheHitRatioText { get; set; } = "—";
    [ObservableProperty] public partial string CacheBytesSavedText { get; set; } = "—";
    // Security tab
    [ObservableProperty] public partial ISeries[] SecuritySeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] SecurityXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] SecurityYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial string TotalThreatsText { get; set; } = "—";
    // Status codes tab
    [ObservableProperty] public partial ISeries[] StatusCodesSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] StatusCodesXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] StatusCodesYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial string StatusSuccessRateText { get; set; } = "—";
    [ObservableProperty] public partial string StatusClientErrorsText { get; set; } = "—";
    [ObservableProperty] public partial string StatusServerErrorsText { get; set; } = "—";
    [ObservableProperty] public partial string StatusTopIssueText { get; set; } = "None";
    [ObservableProperty] public partial List<StatusCodeIssueRow> StatusTopCodes { get; set; } = new();
    // Web Analytics tab
    [ObservableProperty] public partial ISeries[] WaSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] WaXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] WaYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial List<BarRow> WaReferrers { get; set; } = new();
    [ObservableProperty] public partial List<BarRow> WaPages { get; set; } = new();
    [ObservableProperty] public partial List<BarRow> WaCountries { get; set; } = new();
    public CloudflareViewModel(ShellViewModel shell, CloudflareRepository cfRepo, WebAnalyticsRepository waRepo)
    {
        _shell = shell;
        _cfRepo = cfRepo;
        _waRepo = waRepo;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

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
            CreateLine("Visitors", rows.Select(r => r.UniqueVisitors).ToArray(), ChartPalette.Accent, fill: true),
            CreateLine("Pageviews", rows.Select(r => r.Pageviews).ToArray(), ChartPalette.Muted, fill: false),
        };
        TrafficXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        TrafficYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };

        TrafficCountries = countries.Select(c => new BarRow { Label = c.Country, Value = c.Value }).ToList();
        TrafficPages = pages.Select(p => new BarRow { Label = p.Path, Value = p.Pageviews }).ToList();
    }

    private void BuildCacheTab(IReadOnlyList<CacheDay> rows)
    {
        var labels = rows.Select(r => ShortDate(r.Date)).ToArray();
        var hitPct = rows.Select(r => r.HitRatio * 100.0).ToArray();
        CacheSeries = new ISeries[]
        {
            CreateLine("Hit ratio %", hitPct, ChartPalette.Accent, fill: true),
        };
        CacheXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        CacheYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0, MaxLimit = 100 }) };

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
            CreateLine("Threats", rows.Select(r => r.Threats).ToArray(), ChartPalette.Secondary, fill: true),
        };
        SecurityXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        SecurityYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
        TotalThreatsText = rows.Sum(r => r.Threats).ToString("N0", CultureInfo.CurrentCulture);
    }

    private void BuildStatusCodesTab(IReadOnlyList<StatusCodeDay> rows)
    {
        var insight = BuildStatusCodeInsight(rows);

        StatusCodesSeries = insight.Groups.Select(group => new StackedColumnSeries<long>
        {
            Name = group.Label,
            Values = group.Values,
            Fill = new SolidColorPaint(StatusCodeGroupColor(group.Group)),
            Stroke = null,
        }).ToArray();
        StatusCodesXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = insight.Labels }) };
        StatusCodesYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
        StatusSuccessRateText = insight.SuccessRateText;
        StatusClientErrorsText = insight.ClientErrorsText;
        StatusServerErrorsText = insight.ServerErrorsText;
        StatusTopIssueText = insight.TopIssueText;
        StatusTopCodes = insight.TopCodes;
    }

    public static StatusCodeInsight BuildStatusCodeInsight(IReadOnlyList<StatusCodeDay> rows)
    {
        var dates = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToArray();
        var labels = dates.Select(ShortDate).ToArray();
        var totalRequests = rows.Sum(r => r.Requests);

        var groups = new[]
        {
            StatusCodeGroup.Success,
            StatusCodeGroup.Redirect,
            StatusCodeGroup.ClientError,
            StatusCodeGroup.ServerError,
        }.Select(group =>
        {
            var values = dates
                .Select(date => rows
                    .Where(row => row.Date == date && ToStatusCodeGroup(row.StatusCode) == group)
                    .Sum(row => row.Requests))
                .ToArray();

            return new StatusCodeGroupTrend
            {
                Group = group,
                Label = StatusCodeGroupLabel(group),
                Total = values.Sum(),
                Values = values,
            };
        }).ToList();

        var issueCodes = rows
            .Where(row => row.StatusCode >= 400)
            .GroupBy(row => row.StatusCode)
            .Select(group => new
            {
                Code = group.Key,
                Requests = group.Sum(row => row.Requests),
            })
            .OrderByDescending(row => row.Requests)
            .ThenBy(row => row.Code)
            .ToList();

        var topCodes = issueCodes
            .Take(6)
            .Select(row => new StatusCodeIssueRow
            {
                Code = row.Code.ToString(CultureInfo.InvariantCulture),
                Description = StatusCodeDescription(row.Code),
                RequestsText = $"{row.Requests.ToString("N0", CultureInfo.InvariantCulture)} requests",
                ShareText = FormatPercent(row.Requests, totalRequests),
            })
            .ToList();

        var successRequests = groups.First(group => group.Group == StatusCodeGroup.Success).Total;
        var clientErrors = groups.First(group => group.Group == StatusCodeGroup.ClientError).Total;
        var serverErrors = groups.First(group => group.Group == StatusCodeGroup.ServerError).Total;
        var topIssue = issueCodes.FirstOrDefault();

        return new StatusCodeInsight
        {
            Labels = labels,
            Groups = groups,
            TopCodes = topCodes,
            SuccessRateText = totalRequests > 0 ? FormatPercent(successRequests, totalRequests) : "—",
            ClientErrorsText = totalRequests > 0 ? $"{clientErrors.ToString("N0", CultureInfo.InvariantCulture)} ({FormatPercent(clientErrors, totalRequests)})" : "—",
            ServerErrorsText = totalRequests > 0 ? $"{serverErrors.ToString("N0", CultureInfo.InvariantCulture)} ({FormatPercent(serverErrors, totalRequests)})" : "—",
            TopIssueText = topIssue is null
                ? "None"
                : $"{topIssue.Code.ToString(CultureInfo.InvariantCulture)} {StatusCodeDescription(topIssue.Code)} - {topIssue.Requests.ToString("N0", CultureInfo.InvariantCulture)} requests",
        };
    }

    private static StatusCodeGroup ToStatusCodeGroup(int statusCode) => statusCode switch
    {
        >= 200 and <= 299 => StatusCodeGroup.Success,
        >= 300 and <= 399 => StatusCodeGroup.Redirect,
        >= 400 and <= 499 => StatusCodeGroup.ClientError,
        _ => StatusCodeGroup.ServerError,
    };

    private static string StatusCodeGroupLabel(StatusCodeGroup group) => group switch
    {
        StatusCodeGroup.Success => "2xx Success",
        StatusCodeGroup.Redirect => "3xx Redirects",
        StatusCodeGroup.ClientError => "4xx Client errors",
        StatusCodeGroup.ServerError => "5xx Server / edge errors",
        _ => "Other",
    };

    private static SKColor StatusCodeGroupColor(StatusCodeGroup group) => group switch
    {
        StatusCodeGroup.Success => ChartPalette.Success,
        StatusCodeGroup.Redirect => ChartPalette.Info,
        StatusCodeGroup.ClientError => ChartPalette.Warning,
        StatusCodeGroup.ServerError => ChartPalette.Danger,
        _ => ChartPalette.Muted,
    };

    private static string StatusCodeDescription(int statusCode) => statusCode switch
    {
        200 => "OK",
        204 => "No Content",
        206 => "Partial Content",
        301 => "Moved Permanently",
        302 => "Found",
        304 => "Not Modified",
        308 => "Permanent Redirect",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        429 => "Too Many Requests",
        499 => "Client Closed Request",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        520 => "Cloudflare Unknown Error",
        521 => "Origin Refused Connection",
        522 => "Origin Connection Timed Out",
        523 => "Origin Unreachable",
        524 => "Origin Timeout",
        525 => "SSL Handshake Failed",
        526 => "Invalid SSL Certificate",
        530 => "Cloudflare Origin Error",
        _ => ToStatusCodeGroup(statusCode) switch
        {
            StatusCodeGroup.Success => "Success",
            StatusCodeGroup.Redirect => "Redirect",
            StatusCodeGroup.ClientError => "Client error",
            StatusCodeGroup.ServerError => "Server / edge error",
            _ => "Status code",
        },
    };

    private static string FormatPercent(long value, long total)
    {
        if (total <= 0) return "—";
        return ((double)value / total * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
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
            CreateLine("Visits", daily.Select(r => r.Visits).ToArray(), ChartPalette.Accent, fill: true),
            CreateLine("Page views", daily.Select(r => r.PageViews).ToArray(), ChartPalette.Muted, fill: false),
        };
        WaXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        WaYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };

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
