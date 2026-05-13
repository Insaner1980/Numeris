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

public sealed class YouTubeAnalyticsClient
{
    public const string YouTubeAnalyticsReadonlyScope = "https://www.googleapis.com/auth/yt-analytics.readonly";

    private const string ReportsEndpoint = "https://youtubeanalytics.googleapis.com/v2/reports";
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<List<YouTubeDailyMetric>> QueryDailyAsync(string accessToken, string channelId, string start, string end, string fetchedAt)
    {
        var report = await QueryAsync(accessToken, channelId, start, end, "day", "views,estimatedMinutesWatched,averageViewDuration,subscribersGained,subscribersLost,likes,comments,shares", "day", null, null).ConfigureAwait(false);
        return report.Rows.Select(row => new YouTubeDailyMetric
        {
            ChannelId = channelId,
            Date = StringAt(row, 0),
            Views = LongAt(row, 1),
            EstimatedMinutesWatched = DoubleAt(row, 2),
            AverageViewDuration = DoubleAt(row, 3),
            SubscribersGained = LongAt(row, 4),
            SubscribersLost = LongAt(row, 5),
            Likes = LongAt(row, 6),
            Comments = LongAt(row, 7),
            Shares = LongAt(row, 8),
            FetchedAt = fetchedAt,
        }).ToList();
    }

    public async Task<List<YouTubeVideoStat>> QueryTopVideosAsync(string accessToken, string channelId, string start, string end, string fetchedAt, int maxResults)
    {
        var report = await QueryAsync(accessToken, channelId, start, end, "video", "views,estimatedMinutesWatched,averageViewDuration,averageViewPercentage,likes,comments,shares,subscribersGained,subscribersLost", "-views", null, maxResults).ConfigureAwait(false);
        return report.Rows.Select(row => new YouTubeVideoStat
        {
            ChannelId = channelId,
            VideoId = StringAt(row, 0),
            PeriodStart = start,
            PeriodEnd = end,
            Views = LongAt(row, 1),
            EstimatedMinutesWatched = DoubleAt(row, 2),
            AverageViewDuration = DoubleAt(row, 3),
            AverageViewPercentage = DoubleAt(row, 4),
            Likes = LongAt(row, 5),
            Comments = LongAt(row, 6),
            Shares = LongAt(row, 7),
            SubscribersGained = LongAt(row, 8),
            SubscribersLost = LongAt(row, 9),
            FetchedAt = fetchedAt,
        }).ToList();
    }

    public Task<List<YouTubeBreakdownRow>> QueryCountriesAsync(string accessToken, string channelId, string start, string end, string fetchedAt)
        => QueryBreakdownAsync(accessToken, channelId, start, end, fetchedAt, "country", "-views", 50);

    public Task<List<YouTubeBreakdownRow>> QueryTrafficSourcesAsync(string accessToken, string channelId, string start, string end, string fetchedAt)
        => QueryBreakdownAsync(accessToken, channelId, start, end, fetchedAt, "insightTrafficSourceType", "-views", 50);

    public Task<List<YouTubeBreakdownRow>> QueryDevicesAsync(string accessToken, string channelId, string start, string end, string fetchedAt)
        => QueryBreakdownAsync(accessToken, channelId, start, end, fetchedAt, "deviceType", "-views", 50);

    public async Task<List<YouTubeRetentionPoint>> QueryRetentionAsync(string accessToken, string channelId, string videoId, string start, string end, string fetchedAt)
    {
        var report = await QueryAsync(accessToken, channelId, start, end, "elapsedVideoTimeRatio", "audienceWatchRatio,relativeRetentionPerformance", "elapsedVideoTimeRatio", $"video=={videoId}", 200).ConfigureAwait(false);
        return report.Rows.Select(row => new YouTubeRetentionPoint
        {
            ChannelId = channelId,
            VideoId = videoId,
            PeriodStart = start,
            PeriodEnd = end,
            ElapsedRatio = DoubleAt(row, 0),
            AudienceWatchRatio = DoubleAt(row, 1),
            RelativeRetentionPerformance = DoubleAt(row, 2),
            FetchedAt = fetchedAt,
        }).ToList();
    }

    private async Task<List<YouTubeBreakdownRow>> QueryBreakdownAsync(
        string accessToken,
        string channelId,
        string start,
        string end,
        string fetchedAt,
        string dimension,
        string sort,
        int maxResults)
    {
        var report = await QueryAsync(accessToken, channelId, start, end, dimension, "views,estimatedMinutesWatched,averageViewDuration", sort, null, maxResults).ConfigureAwait(false);
        return report.Rows.Select(row => new YouTubeBreakdownRow
        {
            ChannelId = channelId,
            PeriodStart = start,
            PeriodEnd = end,
            Dimension = dimension,
            Label = StringAt(row, 0),
            Views = LongAt(row, 1),
            EstimatedMinutesWatched = DoubleAt(row, 2),
            AverageViewDuration = DoubleAt(row, 3),
            FetchedAt = fetchedAt,
        }).ToList();
    }

    private static async Task<ReportResponse> QueryAsync(
        string accessToken,
        string channelId,
        string start,
        string end,
        string dimensions,
        string metrics,
        string? sort,
        string? filters,
        int? maxResults)
    {
        var query = HttpUtility.ParseQueryString("");
        query["ids"] = $"channel=={channelId}";
        query["startDate"] = start;
        query["endDate"] = end;
        query["dimensions"] = dimensions;
        query["metrics"] = metrics;
        if (!string.IsNullOrWhiteSpace(sort)) query["sort"] = sort;
        if (!string.IsNullOrWhiteSpace(filters)) query["filters"] = filters;
        if (maxResults.HasValue) query["maxResults"] = maxResults.Value.ToString(CultureInfo.InvariantCulture);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ReportsEndpoint}?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException("YouTube Analytics API", "reports.query", response.StatusCode, ApiErrorMessage.FromBody(body));
        }

        return JsonSerializer.Deserialize<ReportResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse YouTube Analytics report response");
    }

    private static string StringAt(IReadOnlyList<JsonElement> row, int index)
        => index < row.Count ? row[index].ToString() : "";

    private static long LongAt(IReadOnlyList<JsonElement> row, int index)
        => index < row.Count && row[index].ValueKind is JsonValueKind.Number && row[index].TryGetInt64(out var value)
            ? value
            : (long)Math.Round(DoubleAt(row, index));

    private static double DoubleAt(IReadOnlyList<JsonElement> row, int index)
        => index < row.Count && row[index].ValueKind is JsonValueKind.Number && row[index].TryGetDouble(out var value)
            ? value
            : 0;

    private sealed class ReportResponse
    {
        [JsonPropertyName("rows")]
        public List<List<JsonElement>> Rows { get; set; } = new();
    }
}
