using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;
using Numeris.Models;

internal static class ReportRefreshRegressionTests
{
    public static void FormatsMeasuredZeroCachedBytes()
    {
        var format = typeof(CloudflareViewModel).GetMethod("FormatBytes", BindingFlags.Static | BindingFlags.NonPublic)!;
        Require((string)format.Invoke(null, new object[] { 0L })! == "0 B", "Measured zero cached bytes must remain zero");
        Require((string)format.Invoke(null, new object[] { 1024L })! == 1.0.ToString("0.0", CultureInfo.CurrentCulture) + " KB", "Nonzero bytes must retain their established scale");
        Require((string)format.Invoke(null, new object[] { -1L })! == "—", "Invalid negative byte counts must remain unavailable");
    }

    public static void FormatsCruxPercentilesWithUnits()
    {
        var property = typeof(CruxMetricSummary).GetProperty("P75Text");
        Require(property is not null, "CrUX percentile display must expose its metric unit");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "fi-FI" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                string Format(string metric, double? value) => (string)property!.GetValue(new CruxMetricSummary { Metric = metric, P75 = value })!;
                Require(Format("largest_contentful_paint", 2500) == "2500 ms", "LCP must show milliseconds without rescaling");
                Require(Format("interaction_to_next_paint", 200) == "200 ms", "INP must show milliseconds without rescaling");
                Require(Format("cumulative_layout_shift", 0.12) == 0.12.ToString(CultureInfo.CurrentCulture), "CLS must retain its dimensionless value");
                Require(Format("largest_contentful_paint", 0) == "0 ms" && Format("cumulative_layout_shift", 0) == "0", "Measured zero must remain zero");
                Require(Format("largest_contentful_paint", null) == "—", "Missing percentile must remain unavailable");
            }
            var label = typeof(PerformanceViewModel).GetMethod("MetricLabel", BindingFlags.Static | BindingFlags.NonPublic)!;
            Require((string)label.Invoke(null, new object[] { "largest_contentful_paint" })! == "LCP p75 (ms)", "LCP chart must use the same unit");
            Require((string)label.Invoke(null, new object[] { "interaction_to_next_paint" })! == "INP p75 (ms)", "INP chart must use the same unit");
            Require((string)label.Invoke(null, new object[] { "cumulative_layout_shift" })! == "CLS p75 (unitless)", "CLS chart must distinguish its unit");
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    public static void PreservesMissingPageSpeedScores()
    {
        var rows = new[] { new PageSpeedScorePoint { AnalysisUtc = "2026-10-01", Strategy = "MOBILE", PerformanceScore = 0.9 } };
        var method = typeof(PerformanceViewModel).GetMethod("ScoreValues", BindingFlags.Static | BindingFlags.NonPublic)!;
        var values = (double?[])method.Invoke(null, new object[] { rows, ReportRefreshRegressionTestsInputs.Vector1, "MOBILE" })!;
        Require(values[0] == 90 && values[1] is null, "An unfetched PageSpeed score must stay missing, not become a zero score");
    }

    public static void IncludesEveryPageSpeedUrlInSummary()
    {
        Require(new PageSpeedLatestRun { PerformanceScore = 0.9 }.PerformanceScoreText == "90"
            && new PageSpeedLatestRun().PerformanceScoreText == "—", "PageSpeed table must use the same 0–100 scale as the cards");
        var viewModel = (PerformanceViewModel)RuntimeHelpers.GetUninitializedObject(typeof(PerformanceViewModel));
        var method = typeof(PerformanceViewModel).GetMethod("BuildPageSpeedSummary", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(viewModel, new object[] { new[]
        {
            new PageSpeedLatestRun { Url = "https://first.example/", Strategy = "MOBILE", PerformanceScore = 0.9 },
            new PageSpeedLatestRun { Url = "https://second.example/", Strategy = "MOBILE", PerformanceScore = 0.5 },
        } });
        Require(viewModel.MobileScoreText == "50–90", "Multiple URLs must show their score range instead of an arbitrary first URL");
        Require(viewModel.DesktopScoreText == "—", "An unfetched strategy must remain missing");
    }

    public static void ReadsCruxNumericStrings()
    {
        const string json = """
            {"record":{"metrics":{"cumulative_layout_shift":{
              "histogramTimeseries":[{"start":"0.00","end":"0.10","densities":[0.8,"NaN",null]}],
              "percentilesTimeseries":{"p75s":["0.12",null,"NaN",0.25]}
            }}}}
            """;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            var response = JsonSerializer.Deserialize<CruxHistoryResponse>(json, CruxOptions())!;
            var metric = response.Record!.Metrics!["cumulative_layout_shift"];
            Require(metric.HistogramTimeseries![0].Start == 0, "CLS lower bound must parse");
            Require(metric.HistogramTimeseries[0].End == 0.1, "CLS upper bound must parse");
            Require(metric.PercentilesTimeseries!.P75s![0] == 0.12, "CLS percentile must parse independently of culture");
            Require(metric.PercentilesTimeseries.P75s[1] is null, "Null percentile must stay missing");
            Require(metric.PercentilesTimeseries.P75s[2] is null, "NaN percentile must stay missing");
            Require(metric.PercentilesTimeseries.P75s[3] == 0.25, "Numeric percentile must still parse");
            Require(metric.HistogramTimeseries[0].Densities![1] is null, "NaN density must stay missing");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    public static void RejectsMalformedCruxNumbers()
    {
        foreach (var value in new[] { "\"invalid\"", "\"Infinity\"", "\"0,12\"", "true" })
        {
            var json = "{\"record\":{\"metrics\":{\"cumulative_layout_shift\":{\"percentilesTimeseries\":{\"p75s\":[" + value + "]}}}}}";
            try
            {
                JsonSerializer.Deserialize<CruxHistoryResponse>(json, CruxOptions());
            }
            catch (JsonException)
            {
                continue;
            }
            throw new InvalidOperationException("Malformed CrUX value must be rejected: " + value);
        }
    }

    public static void ShowsSyncFailure(string report)
    {
        using var database = CreateDatabase(configuredTable: false);
        var (viewModel, refresh) = CreateReport(report, database);
        using var disposable = (IDisposable)viewModel;
        refresh().GetAwaiter().GetResult();
        Require(Read<string>(viewModel, "RefreshStatusMessage").Contains("failed", StringComparison.OrdinalIgnoreCase),
            "A failed sync must be visible to the user");
        Require(Read<object>(viewModel, "RefreshSeverity").ToString() == "Error", "Failed sync must have error severity");
        Require(Read<bool>(viewModel, "CanRefresh"), "Refresh must be available after a failed sync");
    }

    public static void ShowsMissingConfiguration(string report)
    {
        using var database = CreateDatabase(configuredTable: true);
        var (viewModel, refresh) = CreateReport(report, database);
        using var disposable = (IDisposable)viewModel;
        refresh().GetAwaiter().GetResult();
        Require(Read<string>(viewModel, "RefreshStatusMessage").Contains("Sources", StringComparison.Ordinal),
            "Missing configuration must direct the user to Sources");
        Require(Read<object>(viewModel, "RefreshSeverity").ToString() == "Warning", "Missing configuration must not claim success");
        Require(Read<bool>(viewModel, "CanRefresh"), "Refresh must be available after an unconfigured sync");
    }

    public static void PreventsConcurrentRefresh(string report)
    {
        using var database = CreateDatabase(configuredTable: true);
        var (viewModel, refresh) = CreateReport(report, database);
        using var disposable = (IDisposable)viewModel;
        var gate = (SemaphoreSlim)typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
        gate.Wait();
        Task pending;
        var enabledNotifications = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "CanRefresh" && Read<bool>(viewModel, "CanRefresh")) enabledNotifications++;
        };
        try
        {
            pending = refresh();
            Require(!Read<bool>(viewModel, "CanRefresh"), "Refresh button must be disabled during sync");
            Require(!pending.IsCompleted, "Sync must wait for the database");
            Require(refresh().IsCompleted, "A duplicate refresh must not queue another sync");
        }
        finally
        {
            gate.Release();
        }
        pending.GetAwaiter().GetResult();
        Require(enabledNotifications == 1, "Refresh must become available only once, after the operation");
    }

    private static JsonSerializerOptions CruxOptions()
        => (JsonSerializerOptions)typeof(CruxClient).GetField("JsonOptions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static SqliteDatabase CreateDatabase(bool configuredTable)
    {
        // Keep regression tests away from the user's database and credential vault.
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        if (configuredTable)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE connections (id TEXT, source TEXT, status TEXT, config TEXT, last_sync TEXT)";
            command.ExecuteNonQuery();
        }
        var database = (SqliteDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SqliteDatabase));
        typeof(SqliteDatabase).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, connection);
        typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(database, new SemaphoreSlim(1, 1));
        return database;
    }

    private static (ObservableObject ViewModel, Func<Task> Refresh) CreateReport(string report, SqliteDatabase database)
    {
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, "test.example");
        var vault = CredentialVaultRegressionTests.CreateVault();
        var connections = new ConnectionsRepository(database, vault);
        if (report == "Bing")
        {
            var repository = new BingRepository(database);
            var sync = new BingWebmasterSyncService(new BingWebmasterClient(), repository, connections, vault);
            var viewModel = new BingViewModel(shell, repository, sync);
            return (viewModel, viewModel.RefreshAsync);
        }
        if (report == "Performance")
        {
            var repository = new PerformanceRepository(database);
            var sync = new PerformanceSyncService(new CruxClient(), new PageSpeedClient(), repository, connections, vault);
            var viewModel = new PerformanceViewModel(shell, repository, sync);
            return (viewModel, viewModel.RefreshAsync);
        }
        var search = new SearchConsoleRepository(database);
        var sitemaps = new SitemapRepository(database);
        var searchSync = new SearchConsoleSyncService(new SearchConsoleClient(), new GoogleOAuthClient(), search, sitemaps, vault, connections);
        var searchViewModel = new SearchConsoleViewModel(shell, search, sitemaps, searchSync);
        return (searchViewModel, searchViewModel.RefreshAsync);
    }

    private static T Read<T>(object target, string property)
        => (T)(target.GetType().GetProperty(property)?.GetValue(target)
            ?? throw new InvalidOperationException("Missing report property: " + property));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class ReportRefreshRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[] { "2026-10-01", "2026-10-02" };
}
