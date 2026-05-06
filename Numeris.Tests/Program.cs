using System;
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
