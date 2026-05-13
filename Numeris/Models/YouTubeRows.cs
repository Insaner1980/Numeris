namespace Numeris.Models;

public sealed class YouTubeChannelInfo
{
    public string ChannelId { get; set; } = "";
    public string Title { get; set; } = "";
    public string UploadsPlaylistId { get; set; } = "";
    public long ViewCount { get; set; }
    public long SubscriberCount { get; set; }
    public long VideoCount { get; set; }
    public string FetchedAt { get; set; } = "";
}

public sealed class YouTubeVideoInfo
{
    public string VideoId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string Title { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public string? Duration { get; set; }
    public string? ThumbnailUrl { get; set; }
    public long ViewCount { get; set; }
    public long LikeCount { get; set; }
    public long CommentCount { get; set; }
    public string FetchedAt { get; set; } = "";
}

public sealed class YouTubeDailyMetric
{
    public string ChannelId { get; set; } = "";
    public string Date { get; set; } = "";
    public long Views { get; set; }
    public double EstimatedMinutesWatched { get; set; }
    public double AverageViewDuration { get; set; }
    public long SubscribersGained { get; set; }
    public long SubscribersLost { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public long Shares { get; set; }
    public string FetchedAt { get; set; } = "";
}

public sealed class YouTubeVideoStat
{
    public string ChannelId { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public string PeriodStart { get; set; } = "";
    public string PeriodEnd { get; set; } = "";
    public long Views { get; set; }
    public double EstimatedMinutesWatched { get; set; }
    public double AverageViewDuration { get; set; }
    public double AverageViewPercentage { get; set; }
    public long Likes { get; set; }
    public long Comments { get; set; }
    public long Shares { get; set; }
    public long SubscribersGained { get; set; }
    public long SubscribersLost { get; set; }
    public string FetchedAt { get; set; } = "";
}

public sealed class YouTubeBreakdownRow
{
    public string ChannelId { get; set; } = "";
    public string PeriodStart { get; set; } = "";
    public string PeriodEnd { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string Label { get; set; } = "";
    public long Views { get; set; }
    public double EstimatedMinutesWatched { get; set; }
    public double AverageViewDuration { get; set; }
    public string FetchedAt { get; set; } = "";
}

public sealed class YouTubeRetentionPoint
{
    public string ChannelId { get; set; } = "";
    public string VideoId { get; set; } = "";
    public string PeriodStart { get; set; } = "";
    public string PeriodEnd { get; set; } = "";
    public double ElapsedRatio { get; set; }
    public double AudienceWatchRatio { get; set; }
    public double RelativeRetentionPerformance { get; set; }
    public string FetchedAt { get; set; } = "";
}

public sealed class YouTubeSyncResult
{
    public string ChannelId { get; set; } = "";
    public string ChannelTitle { get; set; } = "";
    public long VideosSynced { get; set; }
    public long DailyRows { get; set; }
    public long VideoRows { get; set; }
    public long CountryRows { get; set; }
    public long TrafficRows { get; set; }
    public long DeviceRows { get; set; }
    public long RetentionRows { get; set; }
}
