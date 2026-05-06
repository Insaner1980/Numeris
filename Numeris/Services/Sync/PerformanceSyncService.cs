using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Performance;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class PerformanceSyncService
{
    private static readonly string?[] FormFactors = [null, "PHONE", "DESKTOP", "TABLET"];
    private static readonly string[] PageSpeedStrategies = ["MOBILE", "DESKTOP"];

    private readonly CruxClient _cruxClient;
    private readonly PageSpeedClient _pageSpeedClient;
    private readonly PerformanceRepository _performanceRepo;
    private readonly ConnectionsRepository _connectionsRepo;
    private readonly CredentialVault _vault;

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

        var url = (await _performanceRepo.ListUrlsAsync(enabledOnly: true).ConfigureAwait(false)).FirstOrDefault();
        if (url is null)
        {
            return new ConnectionTestResult { Ok = false, Message = "Add at least one performance URL first" };
        }

        try
        {
            var response = await _cruxClient.QueryHistoryAsync(key, "origin", url.Origin, "PHONE").ConfigureAwait(false);
            var metrics = response.Record?.Metrics?.Count ?? 0;
            return new ConnectionTestResult { Ok = true, Message = $"CrUX works for {url.Origin}; {metrics} metrics returned" };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ex.Message };
        }
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
            return new ConnectionTestResult { Ok = false, Message = ex.Message };
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
                result.CruxMetricPoints += await SyncCruxTargetAsync(cruxKey, "origin", origin, fetchedAt).ConfigureAwait(false);
            }
            foreach (var url in urls)
            {
                result.CruxMetricPoints += await SyncCruxTargetAsync(cruxKey, "url", url.Url, fetchedAt).ConfigureAwait(false);
            }
        }

        var pageSpeedKey = _vault.GetPageSpeedApiKey();
        if (!string.IsNullOrWhiteSpace(pageSpeedKey))
        {
            foreach (var url in urls)
            {
                foreach (var strategy in PageSpeedStrategies)
                {
                    var (runs, audits) = await SyncPageSpeedAsync(pageSpeedKey, url.Url, strategy, fetchedAt).ConfigureAwait(false);
                    result.PageSpeedRuns += runs;
                    result.PageSpeedAudits += audits;
                }
            }
        }

        await _connectionsRepo.UpdatePerformanceLastSyncAsync(fetchedAt).ConfigureAwait(false);
        return result;
    }

    private async Task<long> SyncCruxTargetAsync(string apiKey, string targetType, string target, string fetchedAt)
    {
        long rows = 0;
        foreach (var formFactor in FormFactors)
        {
            CruxHistoryResponse response;
            try
            {
                response = await _cruxClient.QueryHistoryAsync(apiKey, targetType, target, formFactor).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var periods = response.Record?.CollectionPeriods?.CollectionPeriods ?? [];
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
                        RawJson = response.RawJson,
                        FetchedAt = fetchedAt,
                    };
                    await _performanceRepo.UpsertCruxMetricAsync(row).ConfigureAwait(false);
                    rows++;
                }
            }
        }
        return rows;
    }

    private async Task<(long runs, long audits)> SyncPageSpeedAsync(string apiKey, string url, string strategy, string fetchedAt)
    {
        using var doc = await _pageSpeedClient.RunAsync(apiKey, url, strategy).ConfigureAwait(false);
        var root = doc.RootElement;
        var analysisUtc = PageSpeedClient.AnalysisTimestamp(root);
        var raw = root.GetRawText();
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
                    DetailsJson = audit.TryGetProperty("details", out var details) ? details.GetRawText() : null,
                });
            }
        }

        await _performanceRepo.UpsertPageSpeedRunAsync(run, audits).ConfigureAwait(false);
        return (1, audits.Count);
    }

    private static double? GetAt(IReadOnlyList<double?>? values, int index)
        => values is not null && index >= 0 && index < values.Count ? values[index] : null;

    private static string FormatScore(double? score)
        => score.HasValue ? (score.Value * 100).ToString("0", CultureInfo.InvariantCulture) : "n/a";
}
