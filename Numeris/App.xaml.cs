using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Numeris.Services.Api;
using Numeris.Services.Auth;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Migration;
using Numeris.Services.Secrets;
using Numeris.Services.Settings;
using Numeris.Services.Sync;
using Numeris.ViewModels;
using Numeris.ViewModels.Sources;
using Numeris.Views;
using System;

namespace Numeris;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = Services.GetRequiredService<SqliteDatabase>();
        var migration = Services.GetRequiredService<LegacyDataMigrationService>();
        migration.ImportAllAsync().GetAwaiter().GetResult();

        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<SqliteDatabase>();
        services.AddSingleton<LegacyDataMigrationService>();
        services.AddSingleton<SettingsStore>();

        services.AddSingleton<SummaryRepository>();
        services.AddSingleton<CloudflareRepository>();
        services.AddSingleton<SearchConsoleRepository>();
        services.AddSingleton<SitemapRepository>();
        services.AddSingleton<WebAnalyticsRepository>();
        services.AddSingleton<HealthRepository>();
        services.AddSingleton<ConnectionsRepository>();
        services.AddSingleton<PerformanceRepository>();
        services.AddSingleton<BingRepository>();
        services.AddSingleton<YouTubeRepository>();

        services.AddSingleton<UptimeClient>();
        services.AddSingleton<SitemapClient>();
        services.AddSingleton<CloudflareGraphqlClient>();
        services.AddSingleton<CloudflareRumClient>();
        services.AddSingleton<SearchConsoleClient>();
        services.AddSingleton<CruxClient>();
        services.AddSingleton<PageSpeedClient>();
        services.AddSingleton<BingWebmasterClient>();
        services.AddSingleton<YouTubeDataClient>();
        services.AddSingleton<YouTubeAnalyticsClient>();
        services.AddSingleton<GoogleOAuthClient>();
        services.AddSingleton<GoogleOAuthFlow>();
        services.AddSingleton<CredentialVault>();
        services.AddSingleton<CloudflareSyncService>();
        services.AddSingleton<WebAnalyticsSyncService>();
        services.AddSingleton<SearchConsoleSyncService>();
        services.AddSingleton<PerformanceSyncService>();
        services.AddSingleton<BingWebmasterSyncService>();
        services.AddSingleton<YouTubeSyncService>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CloudflareViewModel>();
        services.AddTransient<SearchConsoleViewModel>();
        services.AddTransient<BingViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<YouTubeViewModel>();
        services.AddTransient<HealthViewModel>();
        services.AddTransient<CloudflareSourceViewModel>();
        services.AddTransient<WebAnalyticsSourceViewModel>();
        services.AddTransient<SearchConsoleSourceViewModel>();
        services.AddTransient<PerformanceSourceViewModel>();
        services.AddTransient<YouTubeSourceViewModel>();
        services.AddTransient<BingSourceViewModel>();
        services.AddTransient<SourcesViewModel>();

        services.AddSingleton<MainWindow>();

        services.AddTransient<DashboardPage>();
        services.AddTransient<CloudflarePage>();
        services.AddTransient<SearchConsolePage>();
        services.AddTransient<BingPage>();
        services.AddTransient<PerformancePage>();
        services.AddTransient<YouTubePage>();
        services.AddTransient<HealthPage>();
        services.AddTransient<SourcesPage>();

        return services.BuildServiceProvider();
    }
}
