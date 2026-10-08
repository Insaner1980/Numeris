using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Numeris.Models;
using Numeris.Services.Settings;
using Numeris.ViewModels;

internal static class SettingsRegressionTests
{
    public static void RestoresInvalidPeriodWithoutLosingOtherPreferences()
    {
        InTemporaryDirectory(path =>
        {
            File.WriteAllText(path, "{\"selectedPeriod\":999,\"selectedDomain\":\"test.example\",\"lastPage\":\"search\"}");
            var settings = CreateStore(path).Load();
            Require(settings.SelectedPeriod == Period.Last7Days, "Unknown periods must fall back to seven days");
            Require(settings.SelectedDomain == "test.example" && settings.LastPage == "search", "Valid preferences must survive");
        });
    }

    public static void FailedReplacementPreservesPreviousSettings()
    {
        InTemporaryDirectory(path =>
        {
            var store = CreateStore(path);
            store.Save(new ShellSettings { SelectedPeriod = Period.Last30Days });
            var previous = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    store.Save(new ShellSettings { SelectedPeriod = Period.Last90Days });
                    throw new InvalidOperationException("Replacing a locked file must fail");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                Require(File.ReadAllText(path) == previous, "A failed save must preserve the complete previous file");
            }
            Require(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0, "Temporary files must be removed");
            store.Save(new ShellSettings { SelectedPeriod = Period.Last90Days });
            Require(store.Load().SelectedPeriod == Period.Last90Days, "An unlocked save must replace the complete file");
        });
    }

    public static void SaveFailurePreservesSelectionAndReportsRecovery()
    {
        InTemporaryDirectory(path =>
        {
            var store = CreateStore(path);
            store.Save(new ShellSettings());
            var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
            typeof(ShellViewModel).GetField("_settingsStore", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(shell, store);
            typeof(ShellViewModel).GetField("<AvailableDomains>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(shell, SettingsRegressionTestsInputs.Vector1);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                shell.SelectedDomain = "test.example";
                Require(shell.SelectedDomain == "test.example" && shell.HasSettingsError, "Failed saves must preserve and explain the session selection");
                Require(store.Load().SelectedDomain == "all", "Failed saves must not claim persistence");
            }
            shell.SelectedPeriod = Period.Last30Days;
            Require(!shell.HasSettingsError && store.Load().SelectedDomain == "test.example", "A successful later save must clear the warning and save current preferences");
        });
    }

    private static SettingsStore CreateStore(string path)
        => (SettingsStore)Activator.CreateInstance(typeof(SettingsStore), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { path }, culture: null)!;

    private static void InTemporaryDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Numeris-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { test(Path.Combine(directory, "settings.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class SettingsRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[] { "all", "test.example" };
}
