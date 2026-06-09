using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Performance;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class PerformanceSyncService
{
    private static readonly string?[] FormFactors = [null, "PHONE", "DESKTOP", "TABLET"];
    private static readonly string[] PageSpeedStrategies = ["MOBILE", "DESKTOP"];
    private static readonly TimeSpan PageSpeedRequestSpacing = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PageSpeedInitialBackoff = TimeSpan.FromSeconds(2);
    private const int PageSpeedMaxAttempts = 3;

    private readonly CruxClient _cruxClient;
    private readonly PageSpeedClient _pageSpeedClient;
    private readonly PerformanceRepository _performanceRepo;
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;
    private readonly SemaphoreSlim _pageSpeedLimiter = new(1, 1);
    private DateTimeOffset _lastPageSpeedRequestUtc = DateTimeOffset.MinValue;

    public PerformanceSyncService(
        CruxClient cruxClient,
        PageSpeedClient pageSpeedClient,
        PerformanceRepository performanceRepo,
        ConnectionsRepository connectionsRepo,
        CredentialVault vault)
    {
        _cruxClient = cruxClient;
        _pageSpeedClient = pageSpeedClient;
        _performanceRepo = performanceRepo;
        _connectionsRepo = connectionsRepo;
        _vault = vault;
    }

    public Task<List<PerformanceUrlInfo>> ListUrlsAsync()
        => _performanceRepo.ListUrlsAsync();

    public Task AddUrlAsync(string url)
        => _performanceRepo.AddUrlAsync(url);

    public Task DeleteUrlAsync(long id)
        => _performanceRepo.DeleteUrlAsync(id);

    public async Task<ConnectionTestResult> TestCruxAsync()
    {
        var key = _vault.GetCruxApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new ConnectionTestResult { Ok = false, Message = "No CrUX API key saved" };
        }

        var urls = await _performanceRepo.ListUrlsAsync(enabledOnly: true).ConfigureAwait(false);
        if (urls.Count == 0)
        {
            return new ConnectionTestResult { Ok = false, Message = "Add at least one performance URL first" };
        }

        var targets = urls
            .Select(u => new CruxTestTarget("origin", u.Origin))
            .Concat(urls.Select(u => new CruxTestTarget("url", u.Url)))
            .DistinctBy(t => $"{t.Type}:{t.Value}", StringComparer.OrdinalIgnoreCase)
            .ToList();
        var missing = 0;
        foreach (var target in targets)
        {
            try
            {
                var response = await _cruxClient.QueryHistoryAsync(key, target.Type, target.Value, "PHONE").ConfigureAwait(false);
                var metrics = response.Record?.Metrics?.Count ?? 0;
                var suffix = missing == 0
                    ? ""
                    : $"; {missing} configured target(s) had no CrUX field data";
                return new ConnectionTestResult { Ok = true, Message = $"CrUX works for {target.Value}; {metrics} metrics returned{suffix}" };
            }
            catch (ApiRequestException ex) when (ex.IsNotFound)
            {
                missing++;
            }
            catch (Exception ex)
            {
                return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
            }
        }

        return new ConnectionTestResult
        {
            Ok = true,
            Message = $"CrUX API key works, but no CrUX field data was found for {missing} configured target(s). Sync will skip those targets; add higher-traffic URLs if needed.",
        };
    }

    public async Task<ConnectionTestResult> TestPageSpeedAsync()
    {
        var key = _vault.GetPageSpeedApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new ConnectionTestResult { Ok = false, Message = "No PageSpeed API key saved" };
        }

        var url = (await _performanceRepo.ListUrlsAsync(enabledOnly: true).ConfigureAwait(false)).FirstOrDefault();
        if (url is null)
        {
            return new ConnectionTestResult { Ok = false, Message = "Add at least one performance URL first" };
        }

        try
        {
            using var doc = await _pageSpeedClient.RunAsync(key, url.Url, "MOBILE").ConfigureAwait(false);
            var score = PageSpeedClient.Score(doc.RootElement, "performance");
            return new ConnectionTestResult { Ok = true, Message = $"PageSpeed works for {url.Url}; mobile performance score {FormatScore(score)}" };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
        }
    }

    public async Task<PerformanceSyncResult> SyncAsync()
    {
        var urls = await _performanceRepo.ListUrlsAsync(enabledOnly: true).ConfigureAwait(false);
        var result = new PerformanceSyncResult { UrlsSynced = urls.Count };
        var fetchedAt = _connectionsRepo.FormatNow();

        var cruxKey = _vault.GetCruxApiKey();
        if (!string.IsNullOrWhiteSpace(cruxKey))
        {
            var origins = urls.Select(u => u.Origin).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var origin in origins)
            {
                var crux = await SyncCruxTargetAsync(cruxKey, "origin", origin, fetchedAt).ConfigureAwait(false);
                result.CruxMetricPoints += crux.rows;
                result.CruxSkipped += crux.skipped;
            }
            foreach (var url in urls)
            {
                var crux = await SyncCruxTargetAsync(cruxKey, "url", url.Url, fetchedAt).ConfigureAwait(false);
                result.CruxMetricPoints += crux.rows;
                result.CruxSkipped += crux.skipped;
            }
        }

        var pageSpeedKey = _vault.GetPageSpeedApiKey();
        if (!string.IsNullOrWhiteSpace(pageSpeedKey))
        {
            foreach (var url in urls)
            {
                foreach (var strategy in PageSpeedStrategies)
                {
                    try
                    {
                        var (runs, audits) = await SyncPageSpeedAsync(pageSpeedKey, url.Url, strategy, fetchedAt).ConfigureAwait(false);
                        result.PageSpeedRuns += runs;
                        result.PageSpeedAudits += audits;
                    }
                    catch (ApiRequestException ex) when (ex.IsTransient || ex.IsRateLimited)
                    {
                        result.PageSpeedErrors++;
                    }
                    catch (HttpRequestException)
                    {
                        result.PageSpeedErrors++;
                    }
                    catch (TaskCanceledException)
                    {
                        result.PageSpeedErrors++;
                    }
                }
            }
        }

        await _performanceRepo.ApplyPageSpeedRetentionAsync().ConfigureAwait(false);
        await _connectionsRepo.UpdatePerformanceLastSyncAsync(fetchedAt).ConfigureAwait(false);
        return result;
    }

    public async Task<PerformanceSyncResult?> SyncConfiguredAsync()
    {
        var connection = await _connectionsRepo.GetPerformanceAsync().ConfigureAwait(false);
        if (connection is null || (!connection.HasCruxApiKey && !connection.HasPageSpeedApiKey))
        {
            return null;
        }

        return await SyncAsync().ConfigureAwait(false);
    }

    private async Task<(long rows, long skipped)> SyncCruxTargetAsync(string apiKey, string targetType, string target, string fetchedAt)
    {
        long rows = 0;
        long skipped = 0;
        foreach (var formFactor in FormFactors)
        {
            CruxHistoryResponse response;
            try
            {
                response = await _cruxClient.QueryHistoryAsync(apiKey, targetType, target, formFactor).ConfigureAwait(false);
            }
            catch (ApiRequestException ex) when (ex.IsNotFound)
            {
                skipped++;
                continue;
            }

            var periods = response.Record?.CollectionPeriods ?? [];
            if (response.Record?.Metrics is null) continue;

            foreach (var (metricName, metric) in response.Record.Metrics)
            {
                for (var i = 0; i < periods.Count; i++)
                {
                    var period = periods[i];
                    var bins = metric.HistogramTimeseries;
                    var row = new CruxMetricPoint
                    {
                        TargetType = targetType,
                        Target = target,
                        FormFactor = formFactor ?? "ALL",
                        CollectionStart = period.FirstDate?.ToIsoDate() ?? "",
                        CollectionEnd = period.LastDate?.ToIsoDate() ?? "",
                        Metric = metricName,
                        P75 = GetAt(metric.PercentilesTimeseries?.P75s, i),
                        GoodDensity = GetAt(bins?.ElementAtOrDefault(0)?.Densities, i),
                        NeedsImprovementDensity = GetAt(bins?.ElementAtOrDefault(1)?.Densities, i),
                        PoorDensity = GetAt(bins?.ElementAtOrDefault(2)?.Densities, i),
                        RawJson = RawJsonStoragePolicy.TrimRawJson(response.RawJson),
                        FetchedAt = fetchedAt,
                    };
                    await _performanceRepo.UpsertCruxMetricAsync(row).ConfigureAwait(false);
                    rows++;
                }
            }
        }
        return (rows, skipped);
    }

    private async Task<(long runs, long audits)> SyncPageSpeedAsync(string apiKey, string url, string strategy, string fetchedAt)
    {
        using var doc = await RunPageSpeedWithPolicyAsync(apiKey, url, strategy).ConfigureAwait(false);
        var root = doc.RootElement;
        var analysisUtc = PageSpeedClient.AnalysisTimestamp(root);
        var raw = RawJsonStoragePolicy.TrimRawJson(root.GetRawText());
        var run = new PageSpeedRun
        {
            Url = url,
            Strategy = strategy,
            AnalysisUtc = analysisUtc,
            FinalUrl = PageSpeedClient.String(root, "id"),
            PerformanceScore = PageSpeedClient.Score(root, "performance"),
            AccessibilityScore = PageSpeedClient.Score(root, "accessibility"),
            BestPracticesScore = PageSpeedClient.Score(root, "best-practices"),
            SeoScore = PageSpeedClient.Score(root, "seo"),
            WarningsJson = JsonSerializer.Serialize(PageSpeedClient.Warnings(root)),
            RawJson = raw,
            FetchedAt = fetchedAt,
        };

        if (root.TryGetProperty("lighthouseResult", out var lh))
        {
            run.LighthouseVersion = PageSpeedClient.String(lh, "lighthouseVersion");
            if (lh.TryGetProperty("runtimeError", out var runtimeError))
            {
                run.RuntimeError = runtimeError.GetRawText();
            }
        }

        var audits = new List<PageSpeedAudit>();
        if (root.TryGetProperty("lighthouseResult", out lh)
            && lh.TryGetProperty("audits", out var auditObject)
            && auditObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var auditProperty in auditObject.EnumerateObject())
            {
                var audit = auditProperty.Value;
                audits.Add(new PageSpeedAudit
                {
                    Url = url,
                    Strategy = strategy,
                    AnalysisUtc = analysisUtc,
                    AuditId = auditProperty.Name,
                    Title = PageSpeedClient.String(audit, "title"),
                    Score = PageSpeedClient.Number(audit, "score"),
                    NumericValue = PageSpeedClient.Number(audit, "numericValue"),
                    NumericUnit = PageSpeedClient.String(audit, "numericUnit"),
                    DisplayValue = PageSpeedClient.String(audit, "displayValue"),
                    ScoreDisplayMode = PageSpeedClient.String(audit, "scoreDisplayMode"),
                    DetailsJson = audit.TryGetProperty("details", out var details)
                        ? RawJsonStoragePolicy.TrimDetailsJson(details.GetRawText())
                        : null,
                });
            }
        }

        await _performanceRepo.UpsertPageSpeedRunAsync(run, audits).ConfigureAwait(false);
        return (1, audits.Count);
    }

    private async Task<JsonDocument> RunPageSpeedWithPolicyAsync(string apiKey, string url, string strategy)
    {
        await _pageSpeedLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            for (var attempt = 1; attempt <= PageSpeedMaxAttempts; attempt++)
            {
                await ThrottlePageSpeedAsync().ConfigureAwait(false);
                try
                {
                    return await _pageSpeedClient.RunAsync(apiKey, url, strategy).ConfigureAwait(false);
                }
                catch (ApiRequestException ex) when ((ex.IsTransient || ex.IsRateLimited) && attempt < PageSpeedMaxAttempts)
                {
                    await Task.Delay(BackoffForAttempt(attempt)).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < PageSpeedMaxAttempts)
                {
                    await Task.Delay(BackoffForAttempt(attempt)).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (attempt < PageSpeedMaxAttempts)
                {
                    await Task.Delay(BackoffForAttempt(attempt)).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _pageSpeedLimiter.Release();
        }

        throw new InvalidOperationException("PageSpeed retry policy exited without a result");
    }

    private async Task ThrottlePageSpeedAsync()
    {
        var elapsed = DateTimeOffset.UtcNow - _lastPageSpeedRequestUtc;
        var delay = PageSpeedRequestSpacing - elapsed;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }
        _lastPageSpeedRequestUtc = DateTimeOffset.UtcNow;
    }

    private static TimeSpan BackoffForAttempt(int attempt)
        => TimeSpan.FromMilliseconds(PageSpeedInitialBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));

    private static double? GetAt(IReadOnlyList<double?>? values, int index)
        => values is not null && index >= 0 && index < values.Count ? values[index] : null;

    private static string FormatScore(double? score)
        => score.HasValue ? (score.Value * 100).ToString("0", CultureInfo.InvariantCulture) : "n/a";

    private sealed record CruxTestTarget(string Type, string Value);
}
