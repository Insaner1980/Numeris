using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class YouTubeSyncService
{
    public static readonly string[] Scopes =
    {
        YouTubeDataClient.YouTubeReadonlyScope,
        YouTubeAnalyticsClient.YouTubeAnalyticsReadonlyScope,
    };

    private readonly GoogleOAuthClient _oauthClient;
    private readonly YouTubeDataClient _dataClient;
    private readonly YouTubeAnalyticsClient _analyticsClient;
    private readonly YouTubeRepository _youtubeRepo;
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;

    public YouTubeSyncService(
        GoogleOAuthClient oauthClient,
        YouTubeDataClient dataClient,
        YouTubeAnalyticsClient analyticsClient,
        YouTubeRepository youtubeRepo,
        ConnectionsRepository connectionsRepo,
        CredentialVault vault)
    {
        _oauthClient = oauthClient;
        _dataClient = dataClient;
        _analyticsClient = analyticsClient;
        _youtubeRepo = youtubeRepo;
        _connectionsRepo = connectionsRepo;
        _vault = vault;
    }

    public async Task<ConnectionTestResult> TestAsync(string clientId)
    {
        clientId = clientId.Trim();
        var clientSecret = _vault.GetYouTubeClientSecret(clientId) ?? _vault.GetSearchConsoleClientSecret(clientId);
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            return new ConnectionTestResult { Ok = false, Message = "No YouTube client secret saved" };
        }

        var refreshToken = _vault.GetYouTubeRefreshToken(clientId);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new ConnectionTestResult { Ok = false, Message = "No YouTube refresh token saved. Connect YouTube account first." };
        }

        try
        {
            var accessToken = await _oauthClient.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken).ConfigureAwait(false);
            var channel = await _dataClient.GetMineAsync(accessToken, _connectionsRepo.FormatNow()).ConfigureAwait(false);
            return new ConnectionTestResult
            {
                Ok = true,
                Message = $"YouTube authorization works; channel: {channel.Title}",
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
        }
    }

    public async Task<YouTubeSyncResult> SyncAsync(string clientId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var clientSecret = (_vault.GetYouTubeClientSecret(clientId) ?? _vault.GetSearchConsoleClientSecret(clientId))
            ?? throw new InvalidOperationException("No YouTube client secret saved");
        var refreshToken = _vault.GetYouTubeRefreshToken(clientId)
            ?? throw new InvalidOperationException("Not authorized - connect YouTube account first");

        var accessToken = await _oauthClient.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken).ConfigureAwait(false);
        var fetchedAt = _connectionsRepo.FormatNow();
        var channel = await _dataClient.GetMineAsync(accessToken, fetchedAt).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(channel.UploadsPlaylistId))
        {
            throw new InvalidOperationException("YouTube channel did not return an uploads playlist");
        }

        var videoIds = await _dataClient.ListUploadVideoIdsAsync(accessToken, channel.UploadsPlaylistId).ConfigureAwait(false);
        var videos = await _dataClient.ListVideosAsync(accessToken, channel.ChannelId, videoIds, fetchedAt).ConfigureAwait(false);
        await _youtubeRepo.UpsertChannelAndVideosAsync(channel, videos).ConfigureAwait(false);

        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var start = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var daily = await _analyticsClient.QueryDailyAsync(accessToken, channel.ChannelId, start, end, fetchedAt).ConfigureAwait(false);
        var videoStats = await _analyticsClient.QueryTopVideosAsync(accessToken, channel.ChannelId, start, end, fetchedAt, 200).ConfigureAwait(false);
        var countries = await _analyticsClient.QueryCountriesAsync(accessToken, channel.ChannelId, start, end, fetchedAt).ConfigureAwait(false);
        var trafficSources = await _analyticsClient.QueryTrafficSourcesAsync(accessToken, channel.ChannelId, start, end, fetchedAt).ConfigureAwait(false);
        var devices = await _analyticsClient.QueryDevicesAsync(accessToken, channel.ChannelId, start, end, fetchedAt).ConfigureAwait(false);

        var retention = new System.Collections.Generic.List<YouTubeRetentionPoint>();
        var retentionVideos = videos.Take(25).ToList();
        foreach (var video in retentionVideos)
        {
            try
            {
                retention.AddRange(await _analyticsClient.QueryRetentionAsync(accessToken, channel.ChannelId, video.VideoId, start, end, fetchedAt).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _ = ApiErrorMessage.Sanitize(ex);
            }
        }

        await _youtubeRepo.ReplaceAnalyticsAsync(channel.ChannelId, start, end, daily, videoStats, countries, trafficSources, devices, retention).ConfigureAwait(false);
        await _connectionsRepo.UpdateYouTubeLastSyncAsync(
            new YouTubeConnectionConfig
            {
                ClientId = clientId,
                ChannelId = channel.ChannelId,
                ChannelTitle = channel.Title,
                LastValidatedAt = fetchedAt,
            },
            fetchedAt).ConfigureAwait(false);

        return new YouTubeSyncResult
        {
            ChannelId = channel.ChannelId,
            ChannelTitle = channel.Title,
            VideosSynced = videos.Count,
            DailyRows = daily.Count,
            VideoRows = videoStats.Count,
            CountryRows = countries.Count,
            TrafficRows = trafficSources.Count,
            DeviceRows = devices.Count,
            RetentionRows = retention.Count,
        };
    }
}
