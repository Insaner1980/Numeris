using System;
using System.IO;

namespace Numeris.Helpers;

public static class AppPaths
{
    private static readonly Lazy<string> _dataDir = new(ResolveDataDir);

    public static string DataDir => _dataDir.Value;
    public static string DatabasePath => Path.Combine(DataDir, "numeris.db");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string LogsDir => Path.Combine(DataDir, "logs");

    private static string ResolveDataDir()
    {
        string baseDir;
        if (IsPackaged())
        {
            baseDir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            baseDir = Path.Combine(localAppData, "Numeris");
        }

        Directory.CreateDirectory(baseDir);
        return baseDir;
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
