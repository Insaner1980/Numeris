using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Numeris.Helpers;
using Numeris.Models;

namespace Numeris.Services.Settings;

public sealed class SettingsStore
{
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
            if (!File.Exists(AppPaths.SettingsPath))
            {
                return new ShellSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize<ShellSettings>(json, JsonOptions) ?? new ShellSettings();
        }
        catch
        {
            return new ShellSettings();
        }
    }

    public void Save(ShellSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}

public sealed class ShellSettings
{
    public Period SelectedPeriod { get; set; } = Period.Last7Days;
    public string SelectedDomain { get; set; } = "all";
    public string LastPage { get; set; } = "dashboard";
}
