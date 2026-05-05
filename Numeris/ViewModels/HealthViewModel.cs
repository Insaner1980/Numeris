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
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class HealthViewModel : ObservableObject, IDisposable
{
    private static readonly SKColor AccentGold = SKColor.Parse("#D9A24E");
    private static readonly SKColor Coral = SKColor.Parse("#C97B6A");

    private readonly ShellViewModel _shell;
    private readonly HealthRepository _healthRepo;
    private readonly SitemapRepository _sitemapRepo;
    private readonly UptimeClient _uptimeClient;
    private readonly SitemapClient _sitemapClient;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isCheckingUptime;
    [ObservableProperty] private bool _isRefreshingSitemap;
    [ObservableProperty] private string _activeTab = "uptime";

    [ObservableProperty] private ObservableCollection<UptimeDomainStatus> _domains = new();
    [ObservableProperty] private ISeries[] _responseSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _responseXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _responseYAxes = Array.Empty<Axis>();
    [ObservableProperty] private ISeries[] _incidentsSeries = Array.Empty<ISeries>();
    [ObservableProperty] private Axis[] _incidentsXAxes = Array.Empty<Axis>();
    [ObservableProperty] private Axis[] _incidentsYAxes = Array.Empty<Axis>();

    [ObservableProperty] private ObservableCollection<SitemapUrl> _sitemapUrls = new();
    [ObservableProperty] private string _sitemapSummaryText = "—";

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

            ReplaceCollection(Domains, await statusTask);
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

        ResponseSeries = new ISeries[]
        {
            new LineSeries<double>
            {
                Name = "Avg response (ms)",
                Values = responseValues,
                Stroke = new SolidColorPaint(AccentGold) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(AccentGold) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(AccentGold),
                Fill = new SolidColorPaint(AccentGold.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
        };
        ResponseXAxes = new[] { new Axis { Labels = labels } };
        ResponseYAxes = new[] { new Axis { MinLimit = 0 } };

        IncidentsSeries = new ISeries[]
        {
            new ColumnSeries<long>
            {
                Name = "Incidents",
                Values = incidentsValues,
                Fill = new SolidColorPaint(Coral),
            },
        };
        IncidentsXAxes = new[] { new Axis { Labels = labels } };
        IncidentsYAxes = new[] { new Axis { MinLimit = 0 } };
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
