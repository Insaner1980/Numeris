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
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class HealthViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly HealthRepository _healthRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly UptimeClient _uptimeClient;
    private readonly SitemapClient _sitemapClient;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsCheckingUptime { get; set; }
    [ObservableProperty] public partial bool IsRefreshingSitemap { get; set; }
    public bool CanRefresh => !IsLoading;
    public bool CanCheckUptime => !IsCheckingUptime;
    public bool CanRefreshSitemap => !IsRefreshingSitemap;
    [ObservableProperty] public partial string ActiveTab { get; set; } = "uptime";
    [ObservableProperty] public partial ObservableCollection<UptimeDomainStatus> Domains { get; set; } = new();
    [ObservableProperty] public partial ISeries[] ResponseSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] ResponseXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] ResponseYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial ISeries[] IncidentsSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] IncidentsXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] IncidentsYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial ObservableCollection<SitemapUrl> SitemapUrls { get; set; } = new();
    [ObservableProperty] public partial string SitemapSummaryText { get; set; } = "—";
    [ObservableProperty] public partial string SitemapLastUpdatedText { get; set; } = "Last refreshed —";
    [ObservableProperty] public partial string UptimeOverviewText { get; set; } = "No uptime data";
    [ObservableProperty] public partial string UptimeLastCheckedText { get; set; } = "Last check —";
    [ObservableProperty] public partial string ResponseChartSummaryText { get; set; } = "No samples";
    [ObservableProperty] public partial string IncidentChartSummaryText { get; set; } = "No incidents";
    public HealthViewModel(
        ShellViewModel shell,
        HealthRepository healthRepo,
        SitemapRepository sitemapRepo,
        UptimeClient uptimeClient,
        SitemapClient sitemapClient)
    {
        _shell = shell;
        _healthRepo = healthRepo;
        _sitemapRepo = sitemapRepo;
        _uptimeClient = uptimeClient;
        _sitemapClient = sitemapClient;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnIsCheckingUptimeChanged(bool value) => OnPropertyChanged(nameof(CanCheckUptime));
    partial void OnIsRefreshingSitemapChanged(bool value) => OnPropertyChanged(nameof(CanRefreshSitemap));

    public void Dispose() => _shell.PropertyChanged -= OnShellChanged;

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
            var sitemapDomain = domain == "all" ? Helpers.Domains.KnitTools : domain;

            var statusTask = _healthRepo.GetUptimeStatusAsync(domain);
            var dailyTask = _healthRepo.GetUptimeDailyAsync(domain, startStr, endStr);
            var sitemapTask = _sitemapRepo.ListUrlsAsync(sitemapDomain);

            await Task.WhenAll(statusTask, dailyTask, sitemapTask);

            var statuses = await statusTask;
            ReplaceCollection(Domains, statuses);
            UpdateUptimeSummary(statuses);
            BuildUptimeCharts(await dailyTask);
            ReplaceSitemap(await sitemapTask);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CheckUptimeNowAsync()
    {
        IsCheckingUptime = true;
        try
        {
            var domain = _shell.SelectedDomain;
            var domainsToCheck = domain == "all"
                ? new[] { Helpers.Domains.KnitTools, Helpers.Domains.Finnvek }
                : new[] { domain };

            foreach (var d in domainsToCheck)
            {
                var probe = await _uptimeClient.ProbeAsync(d);
                await _healthRepo.RecordUptimeProbeAsync(probe);
            }

            await LoadAsync();
        }
        finally
        {
            IsCheckingUptime = false;
        }
    }

    [RelayCommand]
    public async Task RefreshSitemapAsync()
    {
        IsRefreshingSitemap = true;
        try
        {
            var domain = _shell.SelectedDomain == "all" ? Helpers.Domains.KnitTools : _shell.SelectedDomain;
            var liveUrls = await _sitemapClient.DiscoverUrlsAsync(domain);
            var result = await _healthRepo.RefreshSitemapAsync(domain, liveUrls);
            SitemapSummaryText = $"{result.TotalUrls} URLs ({result.NewUrls} new, {result.RemovedUrls} removed)";
            ReplaceSitemap(await _sitemapRepo.ListUrlsAsync(domain));
        }
        catch (Exception ex)
        {
            SitemapSummaryText = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsRefreshingSitemap = false;
        }
    }

    private void ReplaceSitemap(IReadOnlyList<SitemapUrl> urls)
    {
        var active = urls.Where(u => u.RemovedAt is null).ToList();
        ReplaceCollection(SitemapUrls, active);
        SitemapLastUpdatedText = BuildSitemapLastUpdatedText(active);
        if (string.IsNullOrEmpty(SitemapSummaryText) || SitemapSummaryText == "—")
        {
            SitemapSummaryText = active.Count > 0
                ? $"{active.Count} URLs in sitemap"
                : "No sitemap data — press Refresh to fetch";
        }
    }

    private void BuildUptimeCharts(IReadOnlyList<UptimeCheckDay> rows)
    {
        var dates = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToArray();
        var labels = dates.Select(ShortDate).ToArray();
        var responseDict = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Average(r => r.AvgResponseMs));
        var incidentsDict = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Incidents));
        var responseValues = dates.Select(d => responseDict.TryGetValue(d, out var v) ? v : 0.0).ToArray();
        var incidentsValues = dates.Select(d => incidentsDict.TryGetValue(d, out var v) ? v : 0L).ToArray();
        var maxResponse = responseValues.DefaultIfEmpty(0).Max();
        var maxIncidents = incidentsValues.DefaultIfEmpty(0).Max();
        ResponseChartSummaryText = responseValues.Length > 0
            ? $"{Math.Round(responseValues.Average(), 0).ToString("0", CultureInfo.InvariantCulture)} ms average"
            : "No samples";
        IncidentChartSummaryText = maxIncidents > 0
            ? $"{incidentsValues.Sum().ToString(CultureInfo.InvariantCulture)} total"
            : "No incidents";

        ResponseSeries = new ISeries[]
        {
            new LineSeries<double>
            {
                Name = "Avg response (ms)",
                Values = responseValues,
                Stroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Accent),
                Fill = new SolidColorPaint(ChartPalette.Accent.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
        };
        ResponseXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        ResponseYAxes = new[]
        {
            ChartTheme.StyleYAxis(new Axis
            {
                MinLimit = 0,
                MaxLimit = BuildResponseAxisMax(maxResponse),
            }),
        };

        IncidentsSeries = new ISeries[]
        {
            ChartTheme.CreateMatteColumnSeries("Incidents", incidentsValues),
        };
        IncidentsXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        IncidentsYAxes = new[]
        {
            ChartTheme.StyleYAxis(new Axis
            {
                MinLimit = 0,
                MaxLimit = Math.Max(1, maxIncidents),
            }),
        };
    }

    private void UpdateUptimeSummary(IReadOnlyList<UptimeDomainStatus> statuses)
    {
        if (statuses.Count == 0)
        {
            UptimeOverviewText = "No monitored domains";
            UptimeLastCheckedText = "Last check —";
            return;
        }

        var incidents = statuses.Sum(status => status.Incidents);
        var upCount = statuses.Count(status => string.Equals(status.Status, "up", StringComparison.OrdinalIgnoreCase));
        UptimeOverviewText = $"{upCount}/{statuses.Count} domains up - {incidents} incidents";
        var lastChecked = statuses
            .Select(status => ParseDateTime(status.LastCheckedAt))
            .Where(value => value is not null)
            .Max();
        UptimeLastCheckedText = lastChecked is null
            ? "Last check —"
            : $"Last check {lastChecked.Value.ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture)}";
    }

    private static string BuildSitemapLastUpdatedText(IReadOnlyList<SitemapUrl> urls)
    {
        var lastSeen = urls
            .Select(url => ParseDateTime(url.LastSeenAt))
            .Where(value => value is not null)
            .Max();
        return lastSeen is null
            ? "Last refreshed —"
            : $"Last refreshed {lastSeen.Value.ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture)}";
    }

    private static DateTimeOffset? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static double BuildResponseAxisMax(double maxResponse)
    {
        if (maxResponse <= 0)
        {
            return 100;
        }

        var rounded = Math.Ceiling(maxResponse * 1.2 / 50.0) * 50.0;
        return Math.Max(100, rounded);
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
