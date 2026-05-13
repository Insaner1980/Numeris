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
    public PerformanceSourceViewModel Performance { get; }
    public YouTubeSourceViewModel YouTube { get; }
    public BingSourceViewModel Bing { get; }

    public SourcesViewModel(
        CloudflareSourceViewModel cloudflare,
        WebAnalyticsSourceViewModel webAnalytics,
        SearchConsoleSourceViewModel searchConsole,
        PerformanceSourceViewModel performance,
        YouTubeSourceViewModel YouTube,
        BingSourceViewModel bing)
    {
        Cloudflare = cloudflare;
        WebAnalytics = webAnalytics;
        SearchConsole = searchConsole;
        Performance = performance;
        this.YouTube = YouTube;
        Bing = bing;
    }

    [RelayCommand]
    public Task LoadAsync()
        => Task.WhenAll(
            Cloudflare.LoadAsync(),
            WebAnalytics.LoadAsync(),
            SearchConsole.LoadAsync(),
            Performance.LoadAsync(),
            YouTube.LoadAsync(),
            Bing.LoadAsync());
}
