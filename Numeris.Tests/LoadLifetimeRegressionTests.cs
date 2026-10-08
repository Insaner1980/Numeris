using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Database;
using Numeris.ViewModels;

internal static class LoadLifetimeRegressionTests
{
    public static void ShellLoadsReportReadFailures()
    {
        foreach (var reportType in new[] { typeof(DashboardViewModel), typeof(CloudflareViewModel), typeof(SearchConsoleViewModel),
            typeof(BingViewModel), typeof(PerformanceViewModel) })
        {
            using var database = (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
                .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            database.WriteAsync(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE cloudflare_traffic; DROP TABLE search_console; DROP TABLE bing_rank_traffic; DROP TABLE crux_metric_points";
                command.ExecuteNonQuery();
            }).GetAwaiter().GetResult();
            var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
            typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, "test.example");
            typeof(ShellViewModel).GetField("<SelectedPeriod>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, Period.Last7Days);
            var report = RuntimeHelpers.GetUninitializedObject(reportType);
            foreach (var field in reportType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(ShellViewModel)) field.SetValue(report, shell);
                var constructor = field.FieldType.GetConstructor(new[] { typeof(SqliteDatabase) });
                if (constructor is not null) field.SetValue(report, constructor.Invoke(new object[] { database }));
            }
            ((Task)reportType.GetMethod("LoadFromShellAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(report, null)!).GetAwaiter().GetResult();
            var severity = reportType.GetProperty("RefreshSeverity")!.GetValue(report)!.ToString();
            var message = (string)reportType.GetProperty("RefreshStatusMessage")!.GetValue(report)!;
            if (severity != "Error" || !message.StartsWith("Load failed:", StringComparison.Ordinal)
                || !message.Contains("no such table", StringComparison.Ordinal))
                throw new InvalidOperationException($"{reportType.Name} did not observe its failed shell load");
        }
    }

    public static void DisposedReportsDoNotPublishPendingLoads()
        => CheckPendingLoads(dispose: true);

    public static void SupersededReportsDoNotPublishPendingLoads()
        => CheckPendingLoads(dispose: false);

    private static void CheckPendingLoads(bool dispose)
    {
        foreach (var reportType in new[] { typeof(DashboardViewModel), typeof(CloudflareViewModel), typeof(SearchConsoleViewModel),
            typeof(BingViewModel), typeof(PerformanceViewModel), typeof(HealthViewModel) })
        {
            using var database = (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
                .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
            var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
            typeof(ShellViewModel).GetField("<SelectedDomain>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, "test.example");
            typeof(ShellViewModel).GetField("<SelectedPeriod>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, Period.Last7Days);
            var report = RuntimeHelpers.GetUninitializedObject(reportType);
            foreach (var field in reportType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(ShellViewModel)) field.SetValue(report, shell);
                var constructor = field.FieldType.GetConstructor(new[] { typeof(SqliteDatabase) });
                if (constructor is not null) field.SetValue(report, constructor.Invoke(new object[] { database }));
            }
            var gate = (SemaphoreSlim)typeof(SqliteDatabase).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(database)!;
            gate.Wait();
            Task pending;
            var lateNotifications = 0;
            try
            {
                pending = (Task)reportType.GetMethod("LoadAsync")!.Invoke(report, null)!;
                if (pending.IsCompleted) throw new InvalidOperationException("Load must await the isolated database gate");
                if (!(bool)reportType.GetProperty("IsLoading")!.GetValue(report)!)
                    throw new InvalidOperationException($"{reportType.Name} must expose loading while previous data remains");
                if (dispose) ((IDisposable)report).Dispose();
                else
                {
                    var version = reportType.GetField("_loadVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    version.SetValue(report, (int)version.GetValue(report)! + 1);
                }
                ((INotifyPropertyChanged)report).PropertyChanged += (_, _) => lateNotifications++;
            }
            finally { gate.Release(); }
            pending.GetAwaiter().GetResult();
            if (lateNotifications != 0) throw new InvalidOperationException($"{reportType.Name} published an inactive load");
            ((IDisposable)report).Dispose();
        }
    }
}
