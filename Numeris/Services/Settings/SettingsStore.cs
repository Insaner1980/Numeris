using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Numeris.Helpers;
using Numeris.Models;

namespace Numeris.Services.Settings;

public sealed class SettingsStore
{
    private readonly string? _settingsPath;
    private string SettingsPath => _settingsPath ?? AppPaths.SettingsPath;

    public SettingsStore() { }

    internal SettingsStore(string settingsPath) => _settingsPath = settingsPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public ShellSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new ShellSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<ShellSettings>(json, JsonOptions) ?? new ShellSettings();
            if (!Enum.IsDefined(settings.SelectedPeriod)) settings.SelectedPeriod = Period.Last7Days;
            return settings;
        }
        catch
        {
            return new ShellSettings();
        }
    }

    public void Save(ShellSettings settings)
    {
        var settingsPath = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public sealed class ShellSettings
{
    public Period SelectedPeriod { get; set; } = Period.Last7Days;
    public string SelectedDomain { get; set; } = "all";
    public string LastPage { get; set; } = "dashboard";
}
