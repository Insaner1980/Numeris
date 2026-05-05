using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Numeris.Services.Database;
using Numeris.Services.MockData;
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
        services.AddSingleton<ShellViewModel>();

        services.AddSingleton<MainWindow>();

        services.AddTransient<DashboardPage>();
        services.AddTransient<CloudflarePage>();
        services.AddTransient<SearchConsolePage>();
        services.AddTransient<HealthPage>();
        services.AddTransient<SourcesPage>();

        return services.BuildServiceProvider();
    }
}
