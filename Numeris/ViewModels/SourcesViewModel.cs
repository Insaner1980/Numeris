using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Numeris.Services.Api;
using Numeris.ViewModels.Sources;

namespace Numeris.ViewModels;

public partial class SourcesViewModel : ObservableObject
{
    public CloudflareSourceViewModel Cloudflare { get; }
    public WebAnalyticsSourceViewModel WebAnalytics { get; }
    public SearchConsoleSourceViewModel SearchConsole { get; }
    public PerformanceSourceViewModel Performance { get; }
    public BingSourceViewModel Bing { get; }

    public SourcesViewModel(
        CloudflareSourceViewModel cloudflare,
        WebAnalyticsSourceViewModel webAnalytics,
        SearchConsoleSourceViewModel searchConsole,
        PerformanceSourceViewModel performance,
        BingSourceViewModel bing)
    {
        Cloudflare = cloudflare;
        WebAnalytics = webAnalytics;
        SearchConsole = searchConsole;
        Performance = performance;
        Bing = bing;
    }

    [RelayCommand]
    public Task LoadAsync()
        => Task.WhenAll(
            LoadSourceAsync(Cloudflare.LoadAsync, message => Cloudflare.StatusMessage = message),
            LoadSourceAsync(WebAnalytics.LoadAsync, message => WebAnalytics.StatusMessage = message),
            LoadSourceAsync(SearchConsole.LoadAsync, message => SearchConsole.StatusMessage = message),
            LoadSourceAsync(Performance.LoadAsync, message => Performance.StatusMessage = message),
            LoadSourceAsync(Bing.LoadAsync, message => Bing.StatusMessage = message));

    private static async Task LoadSourceAsync(Func<Task> load, Action<string> reportError)
    {
        try { await load(); }
        catch (Exception ex) { reportError($"Load failed: {ApiErrorMessage.Sanitize(ex)}"); }
    }
}
