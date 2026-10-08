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

public sealed partial class HealthViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private int _loadVersion;
    private bool _disposed;
    private readonly HealthRepository _healthRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly UptimeClient _uptimeClient;
    private readonly SitemapClient _sitemapClient;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsCheckingUptime { get; set; }
    [ObservableProperty] public partial bool IsRefreshingSitemap { get; set; }
    public bool CanRefresh => !IsLoading && !IsCheckingUptime && !IsRefreshingSitemap;
    public bool CanCheckUptime => CanRefresh;
    public bool CanRefreshSitemap => CanRefresh;
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
    [ObservableProperty] public partial string IncidentChartSummaryText { get; set; } = "No failed probes";
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

    partial void OnIsLoadingChanged(bool value) => NotifyActionsChanged();
    partial void OnIsCheckingUptimeChanged(bool value) => NotifyActionsChanged();
    partial void OnIsRefreshingSitemapChanged(bool value) => NotifyActionsChanged();

    private void NotifyActionsChanged()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanCheckUptime));
        OnPropertyChanged(nameof(CanRefreshSitemap));
    }

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
            _ = LoadAsync();
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
            var domains = domain == "all" ? _shell.AvailableDomains.Where(d => d != "all") : new[] { domain };
            var statusTask = Task.WhenAll(domains.Select(_healthRepo.GetUptimeStatusAsync));
            var dailyTask = _healthRepo.GetUptimeDailyAsync(domain, startStr, endStr);
            var sitemapTask = _sitemapRepo.ListUrlsAsync(domain);

            await Task.WhenAll(statusTask, dailyTask, sitemapTask);
            if (_disposed || loadVersion != _loadVersion) return;

            var statuses = (await statusTask).SelectMany(rows => rows).ToList();
            ReplaceCollection(Domains, statuses);
            UpdateUptimeSummary(statuses);
            BuildUptimeCharts(await dailyTask);
            ReplaceSitemap(await sitemapTask);
        }
        catch (Exception ex)
        {
            if (_disposed || loadVersion != _loadVersion) return;
            UptimeOverviewText = $"Load failed: {ApiErrorMessage.Sanitize(ex)}";
            SitemapSummaryText = UptimeOverviewText;
        }
        finally
        {
            if (!_disposed && loadVersion == _loadVersion) IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CheckUptimeNowAsync()
    {
        if (!CanCheckUptime) return;
        IsCheckingUptime = true;
        try
        {
            var domain = _shell.SelectedDomain;
            var domainsToCheck = domain == "all"
                ? _shell.AvailableDomains.Where(d => d != "all").ToArray()
                : new[] { domain };

            foreach (var d in domainsToCheck)
            {
                var probe = await _uptimeClient.ProbeAsync(d);
                await _healthRepo.RecordUptimeProbeAsync(probe);
            }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            UptimeOverviewText = $"Check failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsCheckingUptime = false;
        }
    }

    [RelayCommand]
    public async Task RefreshSitemapAsync()
    {
        if (!CanRefreshSitemap) return;
        IsRefreshingSitemap = true;
        var domain = _shell.SelectedDomain;
        try
        {
            var domains = domain == "all" ? _shell.AvailableDomains.Where(d => d != "all") : new[] { domain };
            var failures = new List<string>();
            long newUrls = 0, removedUrls = 0;
            foreach (var site in domains)
            {
                try
                {
                    var liveUrls = await _sitemapClient.DiscoverUrlsAsync(site);
                    var result = await _healthRepo.RefreshSitemapAsync(site, liveUrls);
                    newUrls += result.NewUrls;
                    removedUrls += result.RemovedUrls;
                }
                catch (Exception ex)
                {
                    failures.Add($"{site}: {ApiErrorMessage.Sanitize(ex)}");
                }
            }
            var storedUrls = await _sitemapRepo.ListUrlsAsync(domain);
            if (_disposed || domain != _shell.SelectedDomain) return;
            ReplaceSitemap(storedUrls);
            SitemapSummaryText = $"{SitemapUrls.Count} URLs ({newUrls} new, {removedUrls} removed)"
                + (failures.Count > 0 ? $". Refresh failed: {string.Join("; ", failures)}" : "");
        }
        catch (Exception ex)
        {
            if (_disposed || domain != _shell.SelectedDomain) return;
            SitemapSummaryText = $"Refresh failed: {ApiErrorMessage.Sanitize(ex)}";
        }
        finally
        {
            IsRefreshingSitemap = false;
        }
    }

    public Task RefreshAsync() => ActiveTab == "sitemap" ? RefreshSitemapAsync() : CheckUptimeNowAsync();

    private void ReplaceSitemap(IReadOnlyList<SitemapUrl> urls)
    {
        var active = urls.Where(u => u.RemovedAt is null).ToList();
        SitemapUrls = new(active);
        SitemapLastUpdatedText = BuildSitemapLastUpdatedText(active);
        SitemapSummaryText = active.Count > 0
            ? $"{active.Count} URLs in sitemap"
            : "No sitemap data — press Refresh to fetch";
    }

    private void BuildUptimeCharts(IReadOnlyList<UptimeCheckDay> rows)
    {
        var dates = rows.Select(r => r.Date).Distinct().OrderBy(d => d).ToArray();
        var labels = dates.Select(ShortDate).ToArray();
        var responseDict = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Average(r => r.AvgResponseMs));
        var incidentsDict = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.Sum(r => r.Incidents));
        var responseValues = dates.Select(d => responseDict.TryGetValue(d, out var v) ? v : null).ToArray();
        var measuredValues = responseValues.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        var incidentsValues = dates.Select(d => incidentsDict.TryGetValue(d, out var v) ? v : 0L).ToArray();
        var maxResponse = measuredValues.DefaultIfEmpty(0).Max();
        var maxIncidents = incidentsValues.DefaultIfEmpty(0).Max();
        ResponseChartSummaryText = measuredValues.Length > 0
            ? $"{Math.Round(measuredValues.Average(), 0).ToString("0", CultureInfo.InvariantCulture)} ms average"
            : "No timed samples";
        IncidentChartSummaryText = maxIncidents > 0
            ? $"{incidentsValues.Sum().ToString(CultureInfo.InvariantCulture)} total"
            : "No failed probes";

        ResponseSeries = new ISeries[]
        {
            new LineSeries<double?>
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
            ChartTheme.CreateMatteColumnSeries("Failed probes", incidentsValues),
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

    private void UpdateUptimeSummary(List<UptimeDomainStatus> statuses)
    {
        if (statuses.Count == 0)
        {
            UptimeOverviewText = "No monitored domains";
            UptimeLastCheckedText = "Last check —";
            return;
        }

        var incidents = statuses.Sum(status => status.Incidents);
        var upCount = statuses.Count(status => string.Equals(status.Status, "up", StringComparison.OrdinalIgnoreCase));
        UptimeOverviewText = $"{upCount}/{statuses.Count} domains up - {incidents} failed probes (all samples)";
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
