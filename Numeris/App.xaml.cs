using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;
using Numeris.Services.MockData;
using Numeris.Services.Secrets;
using Numeris.Services.Sync;
using Numeris.ViewModels;
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
        var db = Services.GetRequiredService<SqliteDatabase>();
        if (!db.HasData())
        {
            var seeder = Services.GetRequiredService<MockSeeder>();
            seeder.SeedAsync().GetAwaiter().GetResult();
        }

        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<SqliteDatabase>();
        services.AddSingleton<MockSeeder>();

        services.AddSingleton<SummaryRepository>();
        services.AddSingleton<CloudflareRepository>();
        services.AddSingleton<SearchConsoleRepository>();
        services.AddSingleton<SitemapRepository>();
        services.AddSingleton<WebAnalyticsRepository>();
        services.AddSingleton<HealthRepository>();
        services.AddSingleton<ConnectionsRepository>();

        services.AddSingleton<UptimeClient>();
        services.AddSingleton<SitemapClient>();
        services.AddSingleton<CloudflareGraphqlClient>();
        services.AddSingleton<CredentialVault>();
        services.AddSingleton<CloudflareSyncService>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CloudflareViewModel>();
        services.AddTransient<SearchConsoleViewModel>();
        services.AddTransient<HealthViewModel>();
        services.AddTransient<SourcesViewModel>();

        services.AddSingleton<MainWindow>();

        services.AddTransient<DashboardPage>();
        services.AddTransient<CloudflarePage>();
        services.AddTransient<SearchConsolePage>();
        services.AddTransient<HealthPage>();
        services.AddTransient<SourcesPage>();

        return services.BuildServiceProvider();
    }
}
