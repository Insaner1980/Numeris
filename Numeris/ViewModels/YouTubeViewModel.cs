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
using Numeris.Models;
using Numeris.Services.Database.Repositories;
using Numeris.Themes;

namespace Numeris.ViewModels;

public partial class YouTubeViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly YouTubeRepository _youtubeRepo;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    public bool CanRefresh => !IsLoading;
    [ObservableProperty] public partial string ActiveTab { get; set; } = "overview";
    [ObservableProperty] public partial string ChannelTitle { get; set; } = "YouTube";
    [ObservableProperty] public partial string ViewsText { get; set; } = "-";
    [ObservableProperty] public partial string WatchTimeText { get; set; } = "-";
    [ObservableProperty] public partial string AverageDurationText { get; set; } = "-";
    [ObservableProperty] public partial string SubscribersNetText { get; set; } = "-";
    [ObservableProperty] public partial YouTubeVideoStat? SelectedRetentionVideo { get; set; }
    [ObservableProperty] public partial ISeries[] DailySeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] DailyXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] DailyYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial ISeries[] RetentionSeries { get; set; } = Array.Empty<ISeries>();
    [ObservableProperty] public partial Axis[] RetentionXAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial Axis[] RetentionYAxes { get; set; } = Array.Empty<Axis>();
    [ObservableProperty] public partial ObservableCollection<YouTubeVideoStat> Videos { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<BarRow> Countries { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<BarRow> TrafficSources { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<BarRow> Devices { get; set; } = new();

    public YouTubeViewModel(ShellViewModel shell, YouTubeRepository youtubeRepo)
    {
        _shell = shell;
        _youtubeRepo = youtubeRepo;
        _shell.PropertyChanged += OnShellChanged;
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    public void Dispose() => _shell.PropertyChanged -= OnShellChanged;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedPeriod))
        {
            _ = LoadAsync();
        }
    }

    partial void OnSelectedRetentionVideoChanged(YouTubeVideoStat? value) => _ = LoadRetentionAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var channel = await _youtubeRepo.GetChannelAsync();
            if (channel is null)
            {
                ChannelTitle = "YouTube";
                ViewsText = WatchTimeText = AverageDurationText = SubscribersNetText = "-";
                DailySeries = Array.Empty<ISeries>();
                RetentionSeries = Array.Empty<ISeries>();
                ReplaceCollection(Videos, Array.Empty<YouTubeVideoStat>());
                ReplaceCollection(Countries, Array.Empty<BarRow>());
                ReplaceCollection(TrafficSources, Array.Empty<BarRow>());
                ReplaceCollection(Devices, Array.Empty<BarRow>());
                return;
            }

            ChannelTitle = channel.Title;
            var (start, end) = CurrentRange();
            var dailyTask = _youtubeRepo.GetDailyAsync(channel.ChannelId, start, end);
            var videosTask = _youtubeRepo.GetVideoStatsAsync(channel.ChannelId, start, end, 200);
            var countriesTask = _youtubeRepo.GetBreakdownAsync("youtube_countries", channel.ChannelId, start, end, 50);
            var trafficTask = _youtubeRepo.GetBreakdownAsync("youtube_traffic_sources", channel.ChannelId, start, end, 50);
            var devicesTask = _youtubeRepo.GetBreakdownAsync("youtube_devices", channel.ChannelId, start, end, 50);

            await Task.WhenAll(dailyTask, videosTask, countriesTask, trafficTask, devicesTask);

            var daily = await dailyTask;
            var videos = await videosTask;
            BuildOverview(daily);
            ReplaceCollection(Videos, videos);
            ReplaceCollection(Countries, ToBars(await countriesTask));
            ReplaceCollection(TrafficSources, ToBars((await trafficTask).Select(row => WithLabel(row, FormatTrafficSource(row.Label))).ToList()));
            ReplaceCollection(Devices, ToBars((await devicesTask).Select(row => WithLabel(row, FormatDevice(row.Label))).ToList()));

            if (SelectedRetentionVideo is null || videos.All(v => v.VideoId != SelectedRetentionVideo.VideoId))
            {
                SelectedRetentionVideo = videos.FirstOrDefault();
            }
            else
            {
                await LoadRetentionAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadRetentionAsync()
    {
        var channel = await _youtubeRepo.GetChannelAsync();
        if (channel is null || SelectedRetentionVideo is null)
        {
            RetentionSeries = Array.Empty<ISeries>();
            return;
        }

        var (start, end) = CurrentRange();
        var rows = await _youtubeRepo.GetRetentionAsync(channel.ChannelId, SelectedRetentionVideo.VideoId, start, end);
        RetentionSeries = new ISeries[]
        {
            new LineSeries<double>
            {
                Name = "Audience watch ratio",
                Values = rows.Select(r => r.AudienceWatchRatio).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Accent),
                Fill = new SolidColorPaint(ChartPalette.Accent.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.25,
            },
        };
        RetentionXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = rows.Select(r => $"{r.ElapsedRatio * 100:0}%").ToArray() }) };
        RetentionYAxes = new[] { ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }) };
    }

    private void BuildOverview(IReadOnlyList<YouTubeDailyMetric> rows)
    {
        var views = rows.Sum(r => r.Views);
        var watchTime = rows.Sum(r => r.EstimatedMinutesWatched);
        var subscribersNet = rows.Sum(r => r.SubscribersGained - r.SubscribersLost);
        var averageDuration = views > 0 ? rows.Sum(r => r.AverageViewDuration * r.Views) / views : 0;

        ViewsText = FormatNumber(views);
        WatchTimeText = $"{watchTime.ToString("N0", CultureInfo.InvariantCulture)} min";
        AverageDurationText = FormatDuration(averageDuration);
        SubscribersNetText = subscribersNet.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture);

        DailySeries = new ISeries[]
        {
            new LineSeries<long>
            {
                Name = "Views",
                Values = rows.Select(r => r.Views).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Accent) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Accent),
                Fill = new SolidColorPaint(ChartPalette.Accent.WithAlpha(40)),
                GeometrySize = 0,
                LineSmoothness = 0.4,
            },
            new LineSeries<double>
            {
                Name = "Watch time",
                Values = rows.Select(r => r.EstimatedMinutesWatched).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Secondary) { StrokeThickness = 2 },
                GeometryStroke = new SolidColorPaint(ChartPalette.Secondary) { StrokeThickness = 2 },
                GeometryFill = new SolidColorPaint(ChartPalette.Secondary),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.4,
                ScalesYAt = 1,
            },
        };
        DailyXAxes = new[] { ChartTheme.StyleXAxis(new Axis { Labels = rows.Select(r => ShortDate(r.Date)).ToArray() }) };
        DailyYAxes = new[]
        {
            ChartTheme.StyleYAxis(new Axis { MinLimit = 0 }),
            ChartTheme.StyleYAxis(new Axis { MinLimit = 0, Position = LiveChartsCore.Measure.AxisPosition.End }),
        };
    }

    private (string Start, string End) CurrentRange()
    {
        var range = _shell.SelectedPeriod.ToDateRange();
        return (
            range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static List<BarRow> ToBars(IEnumerable<YouTubeBreakdownRow> rows)
        => rows.Select(row => new BarRow { Label = row.Label, Value = row.Views }).ToList();

    private static string FormatTrafficSource(string value)
        => value.Replace("_", " ", StringComparison.Ordinal).ToLowerInvariant();

    private static string FormatDevice(string value)
        => value.Replace("_", " ", StringComparison.Ordinal).ToLowerInvariant();

    private static YouTubeBreakdownRow WithLabel(YouTubeBreakdownRow row, string label)
    {
        row.Label = label;
        return row;
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
        => n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatDuration(double seconds)
        => TimeSpan.FromSeconds(seconds).TotalHours >= 1
            ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
