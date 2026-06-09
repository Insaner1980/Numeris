using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Numeris.ViewModels.Sources;

namespace Numeris.ViewModels;

public partial class SourcesViewModel : ObservableObject
{
    public CloudflareSourceViewModel Cloudflare { get; }
    public WebAnalyticsSourceViewModel WebAnalytics { get; }
    public SearchConsoleSourceViewModel SearchConsole { get; }
    public GoogleAnalyticsSourceViewModel GoogleAnalytics { get; }
    public PerformanceSourceViewModel Performance { get; }
    public BingSourceViewModel Bing { get; }

    public SourcesViewModel(
        CloudflareSourceViewModel cloudflare,
        WebAnalyticsSourceViewModel webAnalytics,
        SearchConsoleSourceViewModel searchConsole,
        GoogleAnalyticsSourceViewModel googleAnalytics,
        PerformanceSourceViewModel performance,
        BingSourceViewModel bing)
    {
        Cloudflare = cloudflare;
        WebAnalytics = webAnalytics;
        SearchConsole = searchConsole;
        GoogleAnalytics = googleAnalytics;
        Performance = performance;
        Bing = bing;
    }

    [RelayCommand]
    public Task LoadAsync()
        => Task.WhenAll(
            Cloudflare.LoadAsync(),
            WebAnalytics.LoadAsync(),
            SearchConsole.LoadAsync(),
            GoogleAnalytics.LoadAsync(),
            Performance.LoadAsync(),
            Bing.LoadAsync());
}
