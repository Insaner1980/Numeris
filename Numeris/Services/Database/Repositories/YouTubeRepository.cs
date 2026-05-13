using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Numeris.Models;

namespace Numeris.Services.Database.Repositories;

public sealed class YouTubeRepository
{
    private readonly SqliteDatabase _db;

    public YouTubeRepository(SqliteDatabase db) => _db = db;

    public Task UpsertChannelAndVideosAsync(YouTubeChannelInfo channel, IReadOnlyList<YouTubeVideoInfo> videos)
        => _db.WriteTransactionAsync((connection, transaction) =>
        {
            connection.Execute(
                """
                INSERT INTO youtube_channels
                (channel_id, title, uploads_playlist_id, view_count, subscriber_count, video_count, fetched_at)
                VALUES (@ChannelId, @Title, @UploadsPlaylistId, @ViewCount, @SubscriberCount, @VideoCount, @FetchedAt)
                ON CONFLICT(channel_id) DO UPDATE SET
                    title = excluded.title,
                    uploads_playlist_id = excluded.uploads_playlist_id,
                    view_count = excluded.view_count,
                    subscriber_count = excluded.subscriber_count,
                    video_count = excluded.video_count,
                    fetched_at = excluded.fetched_at
                """,
                channel,
                transaction);

            foreach (var video in videos)
            {
                connection.Execute(
                    """
                    INSERT INTO youtube_videos
                    (video_id, channel_id, title, published_at, duration, thumbnail_url, view_count, like_count, comment_count, fetched_at)
                    VALUES (@VideoId, @ChannelId, @Title, @PublishedAt, @Duration, @ThumbnailUrl, @ViewCount, @LikeCount, @CommentCount, @FetchedAt)
                    ON CONFLICT(video_id) DO UPDATE SET
                        channel_id = excluded.channel_id,
                        title = excluded.title,
                        published_at = excluded.published_at,
                        duration = excluded.duration,
                        thumbnail_url = excluded.thumbnail_url,
                        view_count = excluded.view_count,
                        like_count = excluded.like_count,
                        comment_count = excluded.comment_count,
                        fetched_at = excluded.fetched_at
                    """,
                    video,
                    transaction);
            }
        });

    public Task ReplaceAnalyticsAsync(
        string channelId,
        string start,
        string end,
        IReadOnlyList<YouTubeDailyMetric> daily,
        IReadOnlyList<YouTubeVideoStat> videos,
        IReadOnlyList<YouTubeBreakdownRow> countries,
        IReadOnlyList<YouTubeBreakdownRow> trafficSources,
        IReadOnlyList<YouTubeBreakdownRow> devices,
        IReadOnlyList<YouTubeRetentionPoint> retention)
        => _db.WriteTransactionAsync((connection, transaction) =>
        {
            connection.Execute("DELETE FROM youtube_daily WHERE channel_id = @channelId AND date >= @start AND date <= @end", new { channelId, start, end }, transaction);
            connection.Execute("DELETE FROM youtube_video_stats WHERE channel_id = @channelId AND period_start = @start AND period_end = @end", new { channelId, start, end }, transaction);
            connection.Execute("DELETE FROM youtube_countries WHERE channel_id = @channelId AND period_start = @start AND period_end = @end", new { channelId, start, end }, transaction);
            connection.Execute("DELETE FROM youtube_traffic_sources WHERE channel_id = @channelId AND period_start = @start AND period_end = @end", new { channelId, start, end }, transaction);
            connection.Execute("DELETE FROM youtube_devices WHERE channel_id = @channelId AND period_start = @start AND period_end = @end", new { channelId, start, end }, transaction);
            connection.Execute("DELETE FROM youtube_retention_points WHERE channel_id = @channelId AND period_start = @start AND period_end = @end", new { channelId, start, end }, transaction);

            foreach (var row in daily)
            {
                connection.Execute(
                    """
                    INSERT INTO youtube_daily
                    (channel_id, date, views, estimated_minutes_watched, average_view_duration, subscribers_gained, subscribers_lost, likes, comments, shares, fetched_at)
                    VALUES (@ChannelId, @Date, @Views, @EstimatedMinutesWatched, @AverageViewDuration, @SubscribersGained, @SubscribersLost, @Likes, @Comments, @Shares, @FetchedAt)
                    """,
                    row,
                    transaction);
            }

            foreach (var row in videos)
            {
                connection.Execute(
                    """
                    INSERT INTO youtube_video_stats
                    (channel_id, video_id, period_start, period_end, views, estimated_minutes_watched, average_view_duration, average_view_percentage, likes, comments, shares, subscribers_gained, subscribers_lost, fetched_at)
                    VALUES (@ChannelId, @VideoId, @PeriodStart, @PeriodEnd, @Views, @EstimatedMinutesWatched, @AverageViewDuration, @AverageViewPercentage, @Likes, @Comments, @Shares, @SubscribersGained, @SubscribersLost, @FetchedAt)
                    """,
                    row,
                    transaction);
            }

            InsertBreakdown(connection, transaction, "youtube_countries", "country", countries);
            InsertBreakdown(connection, transaction, "youtube_traffic_sources", "source_type", trafficSources);
            InsertBreakdown(connection, transaction, "youtube_devices", "device_type", devices);

            foreach (var row in retention)
            {
                connection.Execute(
                    """
                    INSERT INTO youtube_retention_points
                    (channel_id, video_id, period_start, period_end, elapsed_ratio, audience_watch_ratio, relative_retention_performance, fetched_at)
                    VALUES (@ChannelId, @VideoId, @PeriodStart, @PeriodEnd, @ElapsedRatio, @AudienceWatchRatio, @RelativeRetentionPerformance, @FetchedAt)
                    """,
                    row,
                    transaction);
            }
        });

    public Task<YouTubeChannelInfo?> GetChannelAsync()
        => _db.ReadAsync(connection =>
            connection.QueryFirstOrDefault<YouTubeChannelInfo>(
                """
                SELECT channel_id AS ChannelId, title AS Title, uploads_playlist_id AS UploadsPlaylistId,
                       view_count AS ViewCount, subscriber_count AS SubscriberCount, video_count AS VideoCount, fetched_at AS FetchedAt
                FROM youtube_channels
                ORDER BY fetched_at DESC
                LIMIT 1
                """));

    public Task<List<YouTubeDailyMetric>> GetDailyAsync(string channelId, string start, string end)
        => _db.ReadAsync(connection => connection.Query<YouTubeDailyMetric>(
            """
            SELECT channel_id AS ChannelId, date AS Date, views AS Views, estimated_minutes_watched AS EstimatedMinutesWatched,
                   average_view_duration AS AverageViewDuration, subscribers_gained AS SubscribersGained,
                   subscribers_lost AS SubscribersLost, likes AS Likes, comments AS Comments, shares AS Shares, fetched_at AS FetchedAt
            FROM youtube_daily
            WHERE channel_id = @channelId AND date >= @start AND date <= @end
            ORDER BY date
            """,
            new { channelId, start, end }).AsList());

    public Task<List<YouTubeVideoStat>> GetVideoStatsAsync(string channelId, string start, string end, int limit)
        => _db.ReadAsync(connection => connection.Query<YouTubeVideoStat>(
            """
            SELECT v.channel_id AS ChannelId, v.video_id AS VideoId, v.title AS Title,
                   v.published_at AS PublishedAt, @start AS PeriodStart, @end AS PeriodEnd,
                   COALESCE(s.views, v.view_count, 0) AS Views,
                   COALESCE(s.estimated_minutes_watched, 0) AS EstimatedMinutesWatched,
                   COALESCE(s.average_view_duration, 0) AS AverageViewDuration,
                   COALESCE(s.average_view_percentage, 0) AS AverageViewPercentage,
                   COALESCE(s.likes, v.like_count, 0) AS Likes,
                   COALESCE(s.comments, v.comment_count, 0) AS Comments,
                   COALESCE(s.shares, 0) AS Shares,
                   COALESCE(s.subscribers_gained, 0) AS SubscribersGained,
                   COALESCE(s.subscribers_lost, 0) AS SubscribersLost,
                   COALESCE(s.fetched_at, v.fetched_at) AS FetchedAt
            FROM youtube_videos v
            LEFT JOIN youtube_video_stats s ON s.video_id = v.video_id
                AND s.channel_id = v.channel_id
                AND s.period_start = @start
                AND s.period_end = @end
            WHERE v.channel_id = @channelId
            ORDER BY COALESCE(s.views, v.view_count, 0) DESC, v.published_at DESC
            LIMIT @limit
            """,
            new { channelId, start, end, limit }).AsList());

    public Task<List<YouTubeBreakdownRow>> GetBreakdownAsync(string table, string channelId, string start, string end, int limit)
    {
        var dimensionColumn = table switch
        {
            "youtube_countries" => "country",
            "youtube_traffic_sources" => "source_type",
            "youtube_devices" => "device_type",
            _ => throw new System.ArgumentOutOfRangeException(nameof(table)),
        };

        return _db.ReadAsync(connection => connection.Query<YouTubeBreakdownRow>(
            $"""
            SELECT channel_id AS ChannelId, period_start AS PeriodStart, period_end AS PeriodEnd,
                   '{dimensionColumn}' AS Dimension, {dimensionColumn} AS Label, views AS Views,
                   estimated_minutes_watched AS EstimatedMinutesWatched, average_view_duration AS AverageViewDuration,
                   fetched_at AS FetchedAt
            FROM {table}
            WHERE channel_id = @channelId AND period_start = @start AND period_end = @end
            ORDER BY views DESC
            LIMIT @limit
            """,
            new { channelId, start, end, limit }).AsList());
    }

    public Task<List<YouTubeRetentionPoint>> GetRetentionAsync(string channelId, string videoId, string start, string end)
        => _db.ReadAsync(connection => connection.Query<YouTubeRetentionPoint>(
            """
            SELECT channel_id AS ChannelId, video_id AS VideoId, period_start AS PeriodStart, period_end AS PeriodEnd,
                   elapsed_ratio AS ElapsedRatio, audience_watch_ratio AS AudienceWatchRatio,
                   relative_retention_performance AS RelativeRetentionPerformance, fetched_at AS FetchedAt
            FROM youtube_retention_points
            WHERE channel_id = @channelId AND video_id = @videoId AND period_start = @start AND period_end = @end
            ORDER BY elapsed_ratio
            """,
            new { channelId, videoId, start, end }).AsList());

    private static void InsertBreakdown(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string table,
        string column,
        IReadOnlyList<YouTubeBreakdownRow> rows)
    {
        foreach (var row in rows)
        {
            connection.Execute(
                $"""
                INSERT INTO {table}
                (channel_id, period_start, period_end, {column}, views, estimated_minutes_watched, average_view_duration, fetched_at)
                VALUES (@ChannelId, @PeriodStart, @PeriodEnd, @Label, @Views, @EstimatedMinutesWatched, @AverageViewDuration, @FetchedAt)
                """,
                row,
                transaction);
        }
    }
}
