using System;
using System.IO;
using Numeris.Services.Performance;

Run("normalizes home page url to origin without trailing slash", () =>
{
    Equal("https://finnvek.com", PerformanceUrl.NormalizeOrigin("https://finnvek.com/"));
    Equal("https://knittoolsapp.com", PerformanceUrl.NormalizeOrigin("https://knittoolsapp.com/"));
});

Run("keeps page url with trailing slash for PageSpeed", () =>
{
    Equal("https://finnvek.com/", PerformanceUrl.NormalizePageUrl("https://finnvek.com"));
    Equal("https://knittoolsapp.com/tools/", PerformanceUrl.NormalizePageUrl(" https://knittoolsapp.com/tools "));
});

Run("viewmodels do not use ObservableProperty fields", () =>
{
    var root = FindRepositoryRoot();
    var viewModelDir = Path.Combine(root, "Numeris", "ViewModels");
    foreach (var file in Directory.EnumerateFiles(viewModelDir, "*.cs"))
    {
        var text = File.ReadAllText(file);
        if (text.Contains("[ObservableProperty] private", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} still uses ObservableProperty on a private field");
        }
    }
});

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void Equal(string expected, string actual)
{
    if (!StringComparer.Ordinal.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'");
    }
}

static string FindRepositoryRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "Numeris.slnx")))
        {
            return dir;
        }
        dir = Directory.GetParent(dir)?.FullName;
    }
    throw new InvalidOperationException("Repository root not found");
}
