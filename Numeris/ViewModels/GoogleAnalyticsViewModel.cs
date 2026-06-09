using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore.Drawing;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Numeris.Controls;
using Numeris.Models;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Sync;
using Numeris.Themes;
using SkiaSharp;

namespace Numeris.ViewModels;

public partial class GoogleAnalyticsViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly GoogleAnalyticsRepository _analyticsRepo;
    private readonly GoogleAnalyticsSyncService _sync;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    public bool CanRefresh => !IsLoading;
    [ObservableProperty] public partial string ActiveTab { get; set; } = "overview";
    [ObservableProperty] public partial ISeries[] OverviewSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] OverviewXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] OverviewYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial ISeries[] DeviceSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] DeviceXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] DeviceYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial string ActiveUsersText { get; set; } = "—";
    [ObservableProperty] public partial string SessionsText { get; set; } = "—";
    [ObservableProperty] public partial string PageViewsText { get; set; } = "—";
    [ObservableProperty] public partial string EngagementRateText { get; set; } = "—";
    [ObservableProperty] public partial string EngagedSessionsText { get; set; } = "—";
    [ObservableProperty] public partial ObservableCollection<GoogleAnalyticsPageRow> Pages { get; set; } = new();
    [ObservableProperty] public partial List<BarRow> Sources { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<GoogleAnalyticsEventRow> Events { get; set; } = new();

    public GoogleAnalyticsViewModel(
        ShellViewModel shell,
        GoogleAnalyticsRepository analyticsRepo,
        GoogleAnalyticsSyncService sync)
    {
        _shell = shell;
        _analyticsRepo = analyticsRepo;
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

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var range = _shell.SelectedPeriod.ToDateRange();
            var start = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var domain = _shell.SelectedDomain;

            var dailyTask = _analyticsRepo.GetDailyAsync(domain, start, end);
            var pagesTask = _analyticsRepo.GetPagesAsync(domain, start, end, 50);
            var sourcesTask = _analyticsRepo.GetSourcesAsync(domain, start, end, 30);
            var eventsTask = _analyticsRepo.GetEventsAsync(domain, start, end, 50);
            var devicesTask = _analyticsRepo.GetDevicesAsync(domain, start, end);
            await Task.WhenAll(dailyTask, pagesTask, sourcesTask, eventsTask, devicesTask);

            BuildOverview(await dailyTask);
            ReplaceCollection(Pages, await pagesTask);
            Sources = (await sourcesTask).Select(row => new BarRow { Label = row.SourceMedium, Value = row.Sessions }).ToList();
            ReplaceCollection(Events, await eventsTask);
            BuildDevices(await devicesTask);
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
            await _sync.SyncConfiguredAsync(_shell.SelectedPeriod.Days());
        }
        finally
        {
            IsLoading = false;
        }

        await LoadAsync();
    }

    private void BuildOverview(IReadOnlyList<GoogleAnalyticsDailyRow> rows)
    {
        var labels = rows.Select(row => ShortDate(row.Date)).ToArray();
        OverviewSeries = new ISeries[]
        {
            CreateLine("Active users", rows.Select(row => row.ActiveUsers).ToArray(), ChartPalette.Accent, fill: true),
            CreateLine("Sessions", rows.Select(row => row.Sessions).ToArray(), ChartPalette.Secondary, fill: false),
            CreateLine("Views", rows.Select(row => row.PageViews).ToArray(), ChartPalette.Muted, fill: false),
        };
        OverviewXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        OverviewYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };

        ActiveUsersText = FormatNumber(rows.Sum(row => row.ActiveUsers));
        SessionsText = FormatNumber(rows.Sum(row => row.Sessions));
        PageViewsText = FormatNumber(rows.Sum(row => row.PageViews));
        var totalSessions = rows.Sum(row => row.Sessions);
        var totalEngaged = rows.Sum(row => row.EngagedSessions);
        EngagementRateText = totalSessions > 0
            ? $"{(double)totalEngaged / totalSessions:P1}"
            : rows.Count > 0 ? $"{rows.Average(row => row.EngagementRate):P1}" : "—";
        EngagedSessionsText = $"{FormatNumber(totalEngaged)} / {FormatNumber(totalSessions)} engaged sessions";
    }

    private void BuildDevices(IReadOnlyList<GoogleAnalyticsDeviceRow> rows)
    {
        var dates = rows.Select(row => row.Date).Distinct().OrderBy(date => date).ToArray();
        var labels = dates.Select(ShortDate).ToArray();
        var devices = rows.Select(row => row.DeviceCategory).Distinct().OrderBy(device => device).ToArray();
        DeviceSeries = devices.Select((device, index) =>
        {
            var byDate = rows.Where(row => row.DeviceCategory == device).ToDictionary(row => row.Date, row => row.Sessions);
            var values = dates.Select(date => byDate.TryGetValue(date, out var sessions) ? sessions : 0L).ToArray();
            return index == 0
                ? ChartTheme.CreateMatteColumnSeries(device, values)
                : ChartTheme.CreateMutedColumnSeries(device, values);
        }).ToArray();
        DeviceXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = labels }) };
        DeviceYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
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
            GeometrySize = 6,
            DataPadding = new LvcPoint(0.35f, 0.35f),
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

    private static string FormatNumber(long value)
        => value.ToString("N0", CultureInfo.CurrentCulture);

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
