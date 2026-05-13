using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Numeris.Models;

namespace Numeris.Services.Api;

public sealed class YouTubeDataClient
{
    public const string YouTubeReadonlyScope = "https://www.googleapis.com/auth/youtube.readonly";

    private const string ChannelsEndpoint = "https://www.googleapis.com/youtube/v3/channels";
    private const string PlaylistItemsEndpoint = "https://www.googleapis.com/youtube/v3/playlistItems";
    private const string VideosEndpoint = "https://www.googleapis.com/youtube/v3/videos";
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<YouTubeChannelInfo> GetMineAsync(string accessToken, string fetchedAt)
    {
        var url = $"{ChannelsEndpoint}?part=snippet,contentDetails,statistics&mine=true";
        var parsed = await GetAsync<ChannelsResponse>(accessToken, url, "channels.list").ConfigureAwait(false);
        var item = parsed.Items.FirstOrDefault()
            ?? throw new InvalidOperationException("YouTube account returned no channel");
        return new YouTubeChannelInfo
        {
            ChannelId = item.Id,
            Title = item.Snippet?.Title ?? item.Id,
            UploadsPlaylistId = item.ContentDetails?.RelatedPlaylists?.Uploads ?? "",
            ViewCount = ParseLong(item.Statistics?.ViewCount),
            SubscriberCount = ParseLong(item.Statistics?.SubscriberCount),
            VideoCount = ParseLong(item.Statistics?.VideoCount),
            FetchedAt = fetchedAt,
        };
    }

    public async Task<List<string>> ListUploadVideoIdsAsync(string accessToken, string uploadsPlaylistId, int maxVideos = 200)
    {
        var result = new List<string>();
        string? pageToken = null;
        do
        {
            var url = $"{PlaylistItemsEndpoint}?part=contentDetails&playlistId={HttpUtility.UrlEncode(uploadsPlaylistId)}&maxResults=50";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += $"&pageToken={HttpUtility.UrlEncode(pageToken)}";
            }

            var parsed = await GetAsync<PlaylistItemsResponse>(accessToken, url, "playlistItems.list").ConfigureAwait(false);
            result.AddRange(parsed.Items
                .Select(item => item.ContentDetails?.VideoId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!));
            pageToken = parsed.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken) && result.Count < maxVideos);

        return result.Take(maxVideos).ToList();
    }

    public async Task<List<YouTubeVideoInfo>> ListVideosAsync(string accessToken, string channelId, IReadOnlyList<string> videoIds, string fetchedAt)
    {
        var result = new List<YouTubeVideoInfo>();
        foreach (var chunk in videoIds.Chunk(50))
        {
            var ids = string.Join(",", chunk);
            var url = $"{VideosEndpoint}?part=snippet,contentDetails,statistics&id={HttpUtility.UrlEncode(ids)}&maxResults=50";
            var parsed = await GetAsync<VideosResponse>(accessToken, url, "videos.list").ConfigureAwait(false);
            result.AddRange(parsed.Items.Select(item => new YouTubeVideoInfo
            {
                VideoId = item.Id,
                ChannelId = channelId,
                Title = item.Snippet?.Title ?? item.Id,
                PublishedAt = item.Snippet?.PublishedAt ?? "",
                Duration = item.ContentDetails?.Duration,
                ThumbnailUrl = item.Snippet?.Thumbnails?.Medium?.Url
                    ?? item.Snippet?.Thumbnails?.Default?.Url
                    ?? item.Snippet?.Thumbnails?.High?.Url,
                ViewCount = ParseLong(item.Statistics?.ViewCount),
                LikeCount = ParseLong(item.Statistics?.LikeCount),
                CommentCount = ParseLong(item.Statistics?.CommentCount),
                FetchedAt = fetchedAt,
            }));
        }

        return result;
    }

    private static async Task<T> GetAsync<T>(string accessToken, string url, string operation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("YouTube Data API", operation, response.StatusCode, ApiErrorMessage.FromBody(body));
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse YouTube Data API response for {operation}");
    }

    private static long ParseLong(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private sealed class ChannelsResponse { public List<ChannelItem> Items { get; set; } = new(); }
    private sealed class ChannelItem
    {
        public string Id { get; set; } = "";
        public ChannelSnippet? Snippet { get; set; }
        public ChannelContentDetails? ContentDetails { get; set; }
        public ChannelStatistics? Statistics { get; set; }
    }
    private sealed class ChannelSnippet { public string? Title { get; set; } }
    private sealed class ChannelContentDetails { public RelatedPlaylists? RelatedPlaylists { get; set; } }
    private sealed class RelatedPlaylists { public string? Uploads { get; set; } }
    private sealed class ChannelStatistics
    {
        public string? ViewCount { get; set; }
        public string? SubscriberCount { get; set; }
        public string? VideoCount { get; set; }
    }
    private sealed class PlaylistItemsResponse
    {
        public string? NextPageToken { get; set; }
        public List<PlaylistItem> Items { get; set; } = new();
    }
    private sealed class PlaylistItem { public PlaylistContentDetails? ContentDetails { get; set; } }
    private sealed class PlaylistContentDetails { public string? VideoId { get; set; } }
    private sealed class VideosResponse { public List<VideoItem> Items { get; set; } = new(); }
    private sealed class VideoItem
    {
        public string Id { get; set; } = "";
        public VideoSnippet? Snippet { get; set; }
        public VideoContentDetails? ContentDetails { get; set; }
        public VideoStatistics? Statistics { get; set; }
    }
    private sealed class VideoSnippet
    {
        public string? Title { get; set; }
        public string? PublishedAt { get; set; }
        public VideoThumbnails? Thumbnails { get; set; }
    }
    private sealed class VideoThumbnails
    {
        public Thumbnail? Default { get; set; }
        public Thumbnail? Medium { get; set; }
        public Thumbnail? High { get; set; }
    }
    private sealed class Thumbnail { public string? Url { get; set; } }
    private sealed class VideoContentDetails { public string? Duration { get; set; } }
    private sealed class VideoStatistics
    {
        public string? ViewCount { get; set; }
        public string? LikeCount { get; set; }
        public string? CommentCount { get; set; }
    }
}
