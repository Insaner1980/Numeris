using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class GoogleAnalyticsSyncService
{
    public static readonly string[] Scopes = { GoogleAnalyticsClient.Scope };

    private readonly GoogleOAuthClient _oauthClient;
    private readonly GoogleAnalyticsClient _analyticsClient;
    private readonly GoogleAnalyticsRepository _analyticsRepo;
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;

    public GoogleAnalyticsSyncService(
        GoogleOAuthClient oauthClient,
        GoogleAnalyticsClient analyticsClient,
        GoogleAnalyticsRepository analyticsRepo,
        ConnectionsRepository connectionsRepo,
        CredentialVault vault)
    {
        _oauthClient = oauthClient;
        _analyticsClient = analyticsClient;
        _analyticsRepo = analyticsRepo;
        _connectionsRepo = connectionsRepo;
        _vault = vault;
    }

    public async Task<ConnectionTestResult> TestAsync(string clientId)
    {
        clientId = clientId.Trim();
        var connection = await _connectionsRepo.GetGoogleAnalyticsAsync().ConfigureAwait(false);
        if (connection is null || string.IsNullOrWhiteSpace(connection.PropertyId))
        {
            return new ConnectionTestResult { Ok = false, Message = "Save a GA4 property ID first" };
        }

        var clientSecret = _vault.GetGoogleAnalyticsClientSecret(clientId);
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            return new ConnectionTestResult { Ok = false, Message = "No Google Analytics client secret saved" };
        }

        var refreshToken = _vault.GetGoogleAnalyticsRefreshToken(clientId);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new ConnectionTestResult { Ok = false, Message = "No Google Analytics refresh token saved. Connect Google Analytics first." };
        }

        try
        {
            var accessToken = await _oauthClient.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken).ConfigureAwait(false);
            var today = DateOnly.FromDateTime(DateTime.Today);
            var start = today.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var rows = await _analyticsClient.RunReportAsync(
                accessToken,
                connection.PropertyId,
                start,
                end,
                new[] { "date" },
                new[] { "sessions" },
                1).ConfigureAwait(false);

            return new ConnectionTestResult
            {
                Ok = true,
                Message = rows.Count == 0
                    ? "Google Analytics authorization works, but the selected property returned no session rows"
                    : "Google Analytics authorization works; Data API returned session data",
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
        }
    }

    public async Task<SyncResult> SyncAsync(string clientId, int days)
    {
        days = Math.Clamp(days, 1, 365);
        var connection = await _connectionsRepo.GetGoogleAnalyticsAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Google Analytics is not configured");
        var domain = SiteIdentity.NormalizeDomain(connection.Domain);
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Google Analytics domain is missing");
        }
        if (string.IsNullOrWhiteSpace(connection.PropertyId))
        {
            throw new InvalidOperationException("Google Analytics property ID is missing");
        }

        var clientSecret = _vault.GetGoogleAnalyticsClientSecret(clientId)
            ?? throw new InvalidOperationException("No Google Analytics client secret saved");
        var refreshToken = _vault.GetGoogleAnalyticsRefreshToken(clientId)
            ?? throw new InvalidOperationException("Not authorized - connect Google Analytics first");

        var accessToken = await _oauthClient.RefreshAccessTokenAsync(clientId, clientSecret, refreshToken).ConfigureAwait(false);
        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var start = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var fetchedAt = _connectionsRepo.FormatNow();

        var rollup = new GoogleAnalyticsRollup
        {
            Daily = ToDailyRows(await _analyticsClient.RunReportAsync(
                accessToken,
                connection.PropertyId,
                start,
                end,
                new[] { "date" },
                new[] { "activeUsers", "sessions", "screenPageViews", "engagedSessions", "eventCount", "engagementRate" },
                10000).ConfigureAwait(false)),
            Pages = ToPageRows(await _analyticsClient.RunReportAsync(
                accessToken,
                connection.PropertyId,
                start,
                end,
                new[] { "pagePathPlusQueryString" },
                new[] { "activeUsers", "sessions", "screenPageViews", "engagedSessions", "engagementRate" },
                100).ConfigureAwait(false)),
            Sources = ToSourceRows(await _analyticsClient.RunReportAsync(
                accessToken,
                connection.PropertyId,
                start,
                end,
                new[] { "sessionSourceMedium" },
                new[] { "sessions", "activeUsers", "keyEvents" },
                100).ConfigureAwait(false)),
            Events = ToEventRows(await _analyticsClient.RunReportAsync(
                accessToken,
                connection.PropertyId,
                start,
                end,
                new[] { "eventName" },
                new[] { "eventCount", "keyEvents" },
                100).ConfigureAwait(false)),
            Devices = ToDeviceRows(await _analyticsClient.RunReportAsync(
                accessToken,
                connection.PropertyId,
                start,
                end,
                new[] { "date", "deviceCategory" },
                new[] { "sessions", "activeUsers" },
                10000).ConfigureAwait(false)),
        };

        var records = await _analyticsRepo.UpsertRollupAsync(domain, connection.PropertyId, start, end, rollup, fetchedAt).ConfigureAwait(false);
        await _connectionsRepo.UpdateGoogleAnalyticsLastSyncAsync(
            new GoogleAnalyticsConnectionConfig
            {
                Domain = domain,
                PropertyId = connection.PropertyId,
                ClientId = clientId,
                LastValidatedAt = fetchedAt,
            },
            fetchedAt).ConfigureAwait(false);

        return new SyncResult { Domain = domain, DaysSynced = rollup.Daily.Count, RecordsUpserted = records };
    }

    public async Task<SyncResult?> SyncConfiguredAsync(int days)
    {
        var connection = await _connectionsRepo.GetGoogleAnalyticsAsync().ConfigureAwait(false);
        if (connection is null || string.IsNullOrWhiteSpace(connection.ClientId) || !connection.HasRefreshToken)
        {
            return null;
        }

        return await SyncAsync(connection.ClientId, days).ConfigureAwait(false);
    }

    private static List<GoogleAnalyticsDailyRow> ToDailyRows(IEnumerable<GoogleAnalyticsApiRow> rows)
        => rows.Select(row => new GoogleAnalyticsDailyRow
        {
            Date = NormalizeDate(row.Dimension("date")),
            ActiveUsers = row.LongMetric("activeUsers"),
            Sessions = row.LongMetric("sessions"),
            PageViews = row.LongMetric("screenPageViews"),
            EngagedSessions = row.LongMetric("engagedSessions"),
            EventCount = row.LongMetric("eventCount"),
            EngagementRate = row.DoubleMetric("engagementRate"),
        }).Where(row => !string.IsNullOrWhiteSpace(row.Date)).ToList();

    private static List<GoogleAnalyticsPageRow> ToPageRows(IEnumerable<GoogleAnalyticsApiRow> rows)
        => rows.Select(row => new GoogleAnalyticsPageRow
        {
            PagePath = row.Dimension("pagePathPlusQueryString"),
            ActiveUsers = row.LongMetric("activeUsers"),
            Sessions = row.LongMetric("sessions"),
            PageViews = row.LongMetric("screenPageViews"),
            EngagedSessions = row.LongMetric("engagedSessions"),
            EngagementRate = row.DoubleMetric("engagementRate"),
        }).Where(row => !string.IsNullOrWhiteSpace(row.PagePath)).ToList();

    private static List<GoogleAnalyticsSourceRow> ToSourceRows(IEnumerable<GoogleAnalyticsApiRow> rows)
        => rows.Select(row => new GoogleAnalyticsSourceRow
        {
            SourceMedium = row.Dimension("sessionSourceMedium"),
            Sessions = row.LongMetric("sessions"),
            ActiveUsers = row.LongMetric("activeUsers"),
            KeyEvents = row.LongMetric("keyEvents"),
        }).Where(row => !string.IsNullOrWhiteSpace(row.SourceMedium)).ToList();

    private static List<GoogleAnalyticsEventRow> ToEventRows(IEnumerable<GoogleAnalyticsApiRow> rows)
        => rows.Select(row => new GoogleAnalyticsEventRow
        {
            EventName = row.Dimension("eventName"),
            EventCount = row.LongMetric("eventCount"),
            KeyEvents = row.LongMetric("keyEvents"),
        }).Where(row => !string.IsNullOrWhiteSpace(row.EventName)).ToList();

    private static List<GoogleAnalyticsDeviceRow> ToDeviceRows(IEnumerable<GoogleAnalyticsApiRow> rows)
        => rows.Select(row => new GoogleAnalyticsDeviceRow
        {
            Date = NormalizeDate(row.Dimension("date")),
            DeviceCategory = row.Dimension("deviceCategory"),
            Sessions = row.LongMetric("sessions"),
            ActiveUsers = row.LongMetric("activeUsers"),
        }).Where(row => !string.IsNullOrWhiteSpace(row.Date) && !string.IsNullOrWhiteSpace(row.DeviceCategory)).ToList();

    private static string NormalizeDate(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
        {
            return compact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dashed))
        {
            return dashed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        return "";
    }
}
