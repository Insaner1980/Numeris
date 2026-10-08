using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Globalization;
using Dapper;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Services.Database;
using Numeris.Services.Secrets;
using Numeris.Services.Settings;
using Numeris.ViewModels;
using Numeris.Views;
using Numeris.Controls;
using Numeris.Models;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Api;
using Numeris.Converters;

internal static partial class NativeUiRegressionTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Application.Start(parameters =>
                {
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new TestApplication(error => failure = error);
                });
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new TimeoutException("Isolated WinUI tests did not finish");
        if (failure is not null) throw new InvalidOperationException($"Isolated WinUI tests failed: {failure}", failure);
    }

    private sealed partial class TestApplication : Numeris.App
    {
        private readonly Action<Exception> _fail;
        public TestApplication(Action<Exception> fail) => _fail = fail;

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            Window? window = null;
            var directory = Path.Combine(Path.GetTempPath(), "Numeris-ui-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using var database = (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
                    .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                var settings = (SettingsStore)Activator.CreateInstance(typeof(SettingsStore),
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new object[] { Path.Combine(directory, "settings.json") }, null)!;
                var services = new ServiceCollection();
                foreach (var type in typeof(Numeris.App).Assembly.GetTypes().Where(type => type.IsPublic && !type.IsAbstract
                    && (type.Namespace?.StartsWith("Numeris.Services", StringComparison.Ordinal) == true
                        || type.Namespace?.StartsWith("Numeris.ViewModels", StringComparison.Ordinal) == true)))
                {
                    if (type.Name.EndsWith("Repository", StringComparison.Ordinal)
                        || type.Name.EndsWith("Service", StringComparison.Ordinal)
                        || type.Name.EndsWith("Client", StringComparison.Ordinal)
                        || type.Name.EndsWith("Flow", StringComparison.Ordinal)
                        || type.Name.EndsWith("ViewModel", StringComparison.Ordinal)) services.AddTransient(type);
                }
                services.AddSingleton(database);
                services.AddSingleton(settings);
                services.AddSingleton(CredentialVaultRegressionTests.CreateVault());
                services.AddSingleton<ShellViewModel>();
                using var provider = services.BuildServiceProvider();
                typeof(Numeris.App).GetProperty(nameof(Services))!.SetValue(null, provider);
                window = new Window();
                window.Activate();
                await CheckPagesAsync(window, provider);
                await SeedReportsAsync(database);
                await CheckPagesAsync(window, provider);
                CheckControls(window);
                CheckConverters();
                window.Content = null;
            }
            catch (Exception ex) { _fail(ex); }
            finally
            {
                window?.Close();
                Directory.Delete(directory, recursive: true);
                Exit();
            }
        }
    }

    private static async Task CheckPagesAsync(Window window, ServiceProvider provider)
    {
        foreach (var type in ReportPages)
        {
            var page = (Page)Activator.CreateInstance(type)!;
            window.Content = page;
            await Task.Delay(100);
            Require(page.IsLoaded, type.Name + " must load in the native visual tree");
            var model = type.GetProperty("ViewModel")!.GetValue(page)!;
            await (Task)model.GetType().GetMethod("LoadAsync")!.Invoke(model, null)!;
            var loading = model.GetType().GetProperty("IsLoading");
            Require(loading is null || !(bool)loading.GetValue(model)!, type.Name + " must finish loading");

            if (page is CloudflarePage)
            {
                var chartHost = (Border)page.FindName("TrafficChartHost");
                var trafficChart = ((Grid)chartHost.Child).Children.OfType<LiveChartsCore.SkiaSharpView.WinUI.CartesianChart>().Single();
                Require(trafficChart.Series.Count() > 1 && trafficChart.LegendPosition == LiveChartsCore.Measure.LegendPosition.Bottom,
                    "The native traffic chart must expose a legend for its multiple series");
                var originalSize = window.AppWindow.Size;
                foreach (var width in new[] { 900, 1400, 900 })
                {
                    window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Ceiling(width * page.XamlRoot.RasterizationScale), originalSize.Height));
                    await Task.Delay(100);
                    foreach (var name in new[] { "TopCountriesCard", "TopPagesCard", "WaReferrersCard", "WaPagesCard", "WaCountriesCard" })
                    {
                        var card = (FrameworkElement)page.FindName(name);
                        Require(Grid.GetColumnSpan(card) == (width < 1180 ? (name.StartsWith("Wa", StringComparison.Ordinal) ? 3 : 2) : 1),
                            "Cloudflare cards must fill the narrow layout and restore separate columns in the wide layout");
                    }
                }
                window.AppWindow.Resize(originalSize);
            }
            if (page is HealthPage)
            {
                var originalSize = window.AppWindow.Size;
                var scroll = (ScrollViewer)page.FindName("HealthContentScrollViewer");
                var list = (ListView)page.FindName("SitemapUrlsList");
                foreach (var height in new[] { 650, 850 })
                {
                    window.AppWindow.Resize(new Windows.Graphics.SizeInt32(originalSize.Width, height));
                    await Task.Delay(100);
                    Require(list.MaxHeight > 0 && Math.Abs(list.MaxHeight - scroll.ActualHeight) < 0.5,
                        "Sitemap rows must retain a positive viewport bound after each native window resize");
                }
                window.AppWindow.Resize(originalSize);
            }

            if (page.FindName("TabBar") is SelectorBar tabs)
            {
                foreach (var tab in tabs.Items)
                {
                    tabs.SelectedItem = tab;
                    var activeTab = model.GetType().GetProperty("ActiveTab");
                    if (activeTab is not null)
                        Require((string)activeTab.GetValue(model)! == (string)tab.Tag, type.Name + " must select the requested report tab");
                }
            }
            if (page.FindName("PeriodSelector") is PeriodSelector period)
            {
                period.SelectedPeriod = Period.Last30Days;
                InvokeHandler(page, "PeriodSelector_SelectionChanged", period, EventArgs.Empty);
                Require(provider.GetRequiredService<ShellViewModel>().SelectedPeriod == Period.Last30Days,
                    "Report period must propagate to the shared shell");
                period.SelectedPeriod = Period.Last7Days;
                InvokeHandler(page, "PeriodSelector_SelectionChanged", period, EventArgs.Empty);
            }
            foreach (var name in FilterNames)
            {
                if (page.FindName(name) is not ComboBox combo || combo.Items.Count < 2) continue;
                combo.SelectedIndex = 1;
                await Task.Delay(30);
                combo.SelectedIndex = 0;
            }
            await (Task)model.GetType().GetMethod("LoadAsync")!.Invoke(model, null)!;
            if (model is DashboardViewModel dashboard)
            {
                Require(dashboard.SearchSeries.Length == 2, "Overview must retain separate Google and Bing series");
            }
            if (model is SourcesViewModel sources)
            {
                await sources.Cloudflare.TestAsync();
                await sources.WebAnalytics.TestAsync();
                await sources.WebAnalytics.DiscoverSitesAsync();
                await sources.SearchConsole.TestAsync();
                await sources.SearchConsole.ConnectAsync();
                await sources.Bing.TestAsync();
                await sources.Performance.TestCruxAsync();
                await sources.Performance.TestPageSpeedAsync();
                Require(sources.Cloudflare.CanRun && sources.Bing.CanRun && sources.Performance.CanRun,
                    "Missing-credential validation must release busy states");
            }
            window.Content = null;
            await Task.Delay(30);
            if (model is IDisposable disposable) disposable.Dispose();
        }
    }

    private static readonly Type[] ReportPages =
    [typeof(DashboardPage), typeof(CloudflarePage), typeof(SearchConsolePage), typeof(BingPage),
        typeof(PerformancePage), typeof(HealthPage), typeof(SourcesPage)];
    private static readonly string[] FilterNames =
    ["DomainCombo", "QuerySortCombo", "QueryFilterCombo", "MetricCombo", "FormFactorCombo"];

    private static async Task SeedReportsAsync(SqliteDatabase database)
    {
        var day = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await database.WriteAsync(connection => connection.Execute("""
            INSERT INTO cloudflare_traffic(domain,date,pageviews,unique_visitors,requests,cached_requests,cached_bytes,total_bytes,threats,fetched_at)
            VALUES ('knittoolsapp.com',@day,500,120,700,400,1000,2000,5,@day);
            INSERT INTO cloudflare_countries(domain,date,country,visitors) VALUES ('knittoolsapp.com',@day,'FI',120);
            INSERT INTO cloudflare_pages(domain,date,path,requests) VALUES ('knittoolsapp.com',@day,'/',700);
            INSERT INTO cloudflare_status_codes VALUES ('knittoolsapp.com',@day,200,680),('knittoolsapp.com',@day,500,20);
            INSERT INTO search_console(site_url,date,kind,query,page,clicks,impressions,ctr,position,country,fetched_at)
            VALUES ('knittoolsapp.com',@day,'daily','','',40,500,0.08,3,'FIN',@day),
                   ('knittoolsapp.com',@day,'query','knitting','',30,400,0.075,4,'FIN',@day),
                   ('knittoolsapp.com',@day,'page','','https://knittoolsapp.com/',40,500,0.08,3,'FIN',@day);
            INSERT INTO search_devices VALUES ('knittoolsapp.com',@day,'MOBILE',30,300,0.1,3);
            INSERT INTO search_page_queries VALUES ('knittoolsapp.com',@day,@day,'https://knittoolsapp.com/','knitting',30,300,0.1,3);
            INSERT INTO web_analytics_daily(domain,date,visits,page_views,fetched_at) VALUES ('knittoolsapp.com',@day,80,130,@day);
            INSERT INTO web_analytics_countries(domain,date,country,visits) VALUES ('knittoolsapp.com',@day,'FI',80);
            INSERT INTO web_analytics_pages(domain,date,path,page_views) VALUES ('knittoolsapp.com',@day,'/',80);
            INSERT INTO web_analytics_referrers(domain,date,referrer,visits) VALUES ('knittoolsapp.com',@day,'search.example',80);
            INSERT INTO sitemap_urls(domain,url,discovered_at,last_seen_at,verdict,coverage_state,last_inspected_at)
            VALUES ('knittoolsapp.com','https://knittoolsapp.com/',@day,@day,'PASS','Indexed',@day);
            """, new { day }));
        var performance = new PerformanceRepository(database);
        await performance.UpsertCruxMetricAsync(new CruxMetricPoint
        {
            TargetType = "origin", Target = "https://knittoolsapp.com", FormFactor = "PHONE",
            CollectionStart = day, CollectionEnd = day, Metric = "largest_contentful_paint", P75 = 2200,
            GoodDensity = 0.8, NeedsImprovementDensity = 0.15, PoorDensity = 0.05, RawJson = "{}", FetchedAt = day,
        });
        await performance.UpsertPageSpeedRunAsync(new PageSpeedRun
        {
            Url = "https://knittoolsapp.com/", Strategy = "MOBILE", AnalysisUtc = day,
            PerformanceScore = 0.85, AccessibilityScore = 0.95, BestPracticesScore = 1, SeoScore = 1,
            RawJson = "{}", FetchedAt = day,
        }, [new PageSpeedAudit { Url = "https://knittoolsapp.com/", Strategy = "MOBILE", AnalysisUtc = day,
            AuditId = "test-audit", Title = "Reduce image size", Score = 0.4, DisplayValue = "100 KiB" }]);
        await new HealthRepository(database).RecordUptimeProbeAsync(new UptimeProbeResult
        { Domain = "knittoolsapp.com", Status = "up", StatusCode = 200, ResponseMs = 120 });
        await new HealthRepository(database).RecordUptimeProbeAsync(new UptimeProbeResult
        { Domain = "knittoolsapp.com", Status = "down", StatusCode = 500 });
        var bing = new BingRepository(database);
        await bing.UpsertRankTrafficWithRawItemAsync("https://knittoolsapp.com/", day, 20, 200, "{}", day,
            new BingRawItem { Method = "GetRankAndTrafficStats", SiteUrl = "https://knittoolsapp.com/", ItemKey = day, RawJson = "{}", FetchedAt = day });
        await bing.UpsertQueryStatsWithRawItemAsync("knitting", day, 20, 200, 2, 3,
            new BingRawItem { Method = "GetQueryStats", SiteUrl = "https://knittoolsapp.com/", ItemKey = day, RawJson = "{}", FetchedAt = day });
        await bing.UpsertPageStatsWithRawItemAsync("https://knittoolsapp.com/", day, 20, 200,
            new BingRawItem { Method = "GetPageStats", SiteUrl = "https://knittoolsapp.com/", ItemKey = day, RawJson = "{}", FetchedAt = day });
    }

    private static void CheckControls(Window window)
    {
        var badge = new DeltaBadge { Value = 12.5, Suffix = "%" };
        Require(badge.DisplayText == "▲ +12.5%", "Positive deltas must retain the sign and unit");
        var positive = badge.DeltaBrush;
        badge.Invert = true;
        Require(!ReferenceEquals(positive, badge.DeltaBrush), "Inverted deltas must change semantic color");
        badge.Value = -2;
        Require(badge.DisplayText == "▼ -2%", "Negative deltas must retain their sign");
        badge.ShowSign = false;
        badge.Value = 0;
        Require(badge.DisplayText == "0%" && badge.DeltaBrush is not null, "Zero delta must be neutral");
        var card = new KpiCard { Label = "Visitors", Value = "120", Detail = "Cloudflare", ChangePct = 10 };
        Require(card.ChangeValue == 10 && card.ShowChange == Visibility.Visible && card.ChangeTooltip.StartsWith("+10%", StringComparison.Ordinal), "Card comparison must be visible");
        card.ChangePct = null;
        Require(card.ChangeValue == 0 && card.ShowChange == Visibility.Collapsed && card.ChangeTooltip == "", "Missing comparisons must remain hidden");
        var rows = new ObservableCollection<BarRow> { new() { Label = "FI", Value = 120 }, new() { Label = "SE", Value = 20 } };
        var bars = new HorizontalBars { Items = rows, Compact = true, HighlightTopValue = false };
        window.Content = bars;
        bars.Measure(new Windows.Foundation.Size(500, 300));
        bars.Arrange(new Windows.Foundation.Rect(0, 0, 500, 300));
        Require(((ItemsControl)bars.FindName("ItemsHost")).Items.Count == 2, "Every bar row must render");
        rows.Clear();
        Require(((ItemsControl)bars.FindName("ItemsHost")).Items.Count == 1, "An empty list must render its empty-state message");
        rows.Add(new BarRow { Label = "Zero", Value = 0 });
        bars.Items = null;
        var period = new PeriodSelector();
        var changes = 0;
        period.SelectionChanged += (_, _) => changes++;
        InvokeHandler(period, "Last30Button_Click", period, new RoutedEventArgs());
        InvokeHandler(period, "Last30Button_Click", period, new RoutedEventArgs());
        Require(period.SelectedPeriod == Period.Last30Days && changes == 1, "Repeated period selection must not notify twice");
        InvokeHandler(period, "Last90Button_Click", period, new RoutedEventArgs());
        InvokeHandler(period, "AllButton_Click", period, new RoutedEventArgs());
        InvokeHandler(period, "Last7Button_Click", period, new RoutedEventArgs());
        Require(period.SelectedPeriod == Period.Last7Days && changes == 4, "Each period must be selectable");
    }

    private static void CheckConverters()
    {
        Require((string)new OneDecimalConverter().Convert(1.25, typeof(string), null!, "en") == "1.3", "Decimals must use invariant formatting");
        Require((string)new OneDecimalConverter().Convert(2.5f, typeof(string), null!, "en") == "2.5", "Float values must be formatted");
        Require((string)new OneDecimalConverter().Convert(null!, typeof(string), null!, "en") == "—", "Missing decimals need a placeholder");
        Require((string)new IsoDateTimeDisplayConverter().Convert("invalid", typeof(string), null!, "en") == "invalid", "Unknown timestamps must remain visible");
        Require(((string)new IsoDateTimeDisplayConverter().Convert("2026-01-02T12:30:00Z", typeof(string), null!, "en")).Contains("2026", StringComparison.Ordinal), "ISO timestamps must render a date");
        Require((string)new IsoDateTimeDisplayConverter().Convert("", typeof(string), null!, "en") == "—", "Missing timestamps need a placeholder");
        Require((string)new StatusTextConverter().Convert("not_found", typeof(string), null!, "en") == "Not Found", "Status labels must be human-readable");
        Require((string)new StatusTextConverter().Convert(null!, typeof(string), null!, "en") == "Unknown", "Missing status must remain unknown");
        Require((string)new MillisecondsConverter().Convert(12L, typeof(string), null!, "en") == "12 ms", "Long durations need a unit");
        Require((string)new MillisecondsConverter().Convert(8, typeof(string), null!, "en") == "8 ms", "Integer durations need a unit");
        Require((string)new MillisecondsConverter().Convert(null!, typeof(string), null!, "en") == "—", "Missing durations need a placeholder");
        Require((string)new EmptyFallbackConverter().Convert("", typeof(string), null!, "en") == "—", "Blank values need a placeholder");
        Require((string)new EmptyFallbackConverter().Convert("value", typeof(string), null!, "en") == "value", "Nonblank values must remain unchanged");
    }

    private static void InvokeHandler(object target, string name, object sender, object args)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [sender, args]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
