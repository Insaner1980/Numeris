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
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;
using Numeris.Themes;

namespace Numeris.ViewModels;

public partial class BingViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly BingRepository _bingRepo;
    private readonly BingWebmasterSyncService _sync;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    public bool CanRefresh => !IsLoading;
    [ObservableProperty] public partial string ActiveTab { get; set; } = "overview";
    [ObservableProperty] public partial string QuerySortBy { get; set; } = "clicks";
    [ObservableProperty] public partial ISeries[] TrafficSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] TrafficXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] TrafficYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial string TotalClicksText { get; set; } = "—";
    [ObservableProperty] public partial string TotalImpressionsText { get; set; } = "—";
    [ObservableProperty] public partial string CtrText { get; set; } = "—";
    [ObservableProperty] public partial ObservableCollection<BingQueryRow> Queries { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<BingPageRow> Pages { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<BingRawMethodSummary> CrawlSummaries { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<BingRawItem> CrawlIssues { get; set; } = new();

    public BingViewModel(ShellViewModel shell, BingRepository bingRepo, BingWebmasterSyncService sync)
    {
        _shell = shell;
        _bingRepo = bingRepo;
        _sync = sync;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    public void Dispose() => _shell.PropertyChanged -= OnShellChanged;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedPeriod) or nameof(ShellViewModel.SelectedDomain))
        {
            _ = LoadAsync();
        }
    }

    partial void OnQuerySortByChanged(string value) => _ = ReloadQueriesAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var range = _shell.SelectedPeriod.ToDateRange();
            var start = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var siteUrl = SelectedBingSiteUrl();

            var trafficTask = _bingRepo.GetTrafficDailyAsync(siteUrl, start, end);
            var queriesTask = _bingRepo.GetQueriesAsync(siteUrl, start, end, QuerySortBy, 100);
            var pagesTask = _bingRepo.GetPagesAsync(siteUrl, start, end, 50);
            var summariesTask = _bingRepo.GetRawMethodSummaryAsync(siteUrl);
            var issuesTask = _bingRepo.GetCrawlIssueItemsAsync(siteUrl, 50);

            await Task.WhenAll(trafficTask, queriesTask, pagesTask, summariesTask, issuesTask);

            var traffic = await trafficTask;
            BuildTraffic(traffic);
            ReplaceCollection(Queries, await queriesTask);
            ReplaceCollection(Pages, await pagesTask);
            ReplaceCollection(CrawlSummaries, await summariesTask);
            ReplaceCollection(CrawlIssues, await issuesTask);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            await _sync.SyncConfiguredAsync();
        }
        catch (Exception ex)
        {
            _ = ApiErrorMessage.Sanitize(ex);
        }
        finally
        {
            IsLoading = false;
        }

        await LoadAsync();
    }

    private async Task ReloadQueriesAsync()
    {
        var range = _shell.SelectedPeriod.ToDateRange();
        var start = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ReplaceCollection(Queries, await _bingRepo.GetQueriesAsync(SelectedBingSiteUrl(), start, end, QuerySortBy, 100));
    }

    private void BuildTraffic(IReadOnlyList<BingTrafficDay> rows)
    {
        TotalClicksText = rows.Sum(r => r.Clicks).ToString("N0", CultureInfo.InvariantCulture);
        TotalImpressionsText = rows.Sum(r => r.Impressions).ToString("N0", CultureInfo.InvariantCulture);
        var impressions = rows.Sum(r => r.Impressions);
        CtrText = impressions > 0
            ? $"{((double)rows.Sum(r => r.Clicks) / impressions * 100.0).ToString("0.0", CultureInfo.InvariantCulture)}%"
            : "—";

        var clicks = ChartTheme.CreateMatteColumnSeries("Clicks", rows.Select(r => r.Clicks).ToArray());
        var impressionsSeries = ChartTheme.CreateMutedColumnSeries("Impressions", rows.Select(r => r.Impressions).ToArray());
        impressionsSeries.ScalesYAt = 1;

        TrafficSeries = new ISeries[] { clicks, impressionsSeries };
        TrafficXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = rows.Select(r => ShortDate(r.Date)).ToArray() }) };
        TrafficYAxes = new[]
        {
            ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }),
            ChartTheme.StyleYAxis(new Axis { MinLimit = 0, Position = LiveChartsCore.Measure.AxisPosition.End }),
        };
    }

    private string SelectedBingSiteUrl()
        => _shell.SelectedDomain == "all" ? "all" : SiteIdentity.NormalizeHomePageUrl(_shell.SelectedDomain);

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
