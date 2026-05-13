using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.ViewModels;

namespace Numeris.Views;

public sealed partial class SourcesPage : Page
{
    public SourcesViewModel ViewModel { get; }

    public SourcesPage()
    {
        ViewModel = App.Services.GetRequiredService<SourcesViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void TestCfButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.Cloudflare.TestAsync();
    }

    private async void TestSavedCfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CloudflareConnectionInfo info)
        {
            await ViewModel.Cloudflare.TestSavedAsync(info);
        }
    }

    private async void SaveCfButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.Cloudflare.SaveAsync();
        CfTokenBox.Password = "";
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CloudflareConnectionInfo info)
        {
            await ViewModel.Cloudflare.SyncAsync(info);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CloudflareConnectionInfo info)
        {
            await ViewModel.Cloudflare.DeleteAsync(info);
        }
    }

    private async void SaveWaButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.WebAnalytics.SaveAsync();
        WaTokenBox.Password = "";
    }

    private async void DiscoverWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.WebAnalytics.DiscoverSitesAsync();

    private async void TestWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.WebAnalytics.TestAsync();

    private async void SyncWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.WebAnalytics.SyncAsync();

    private async void DeleteWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.WebAnalytics.DeleteAsync();

    private async void AddWaSiteButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.WebAnalytics.AddSiteAsync();

    private async void DeleteWaSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WebAnalyticsSite site)
        {
            await ViewModel.WebAnalytics.DeleteSiteAsync(site);
        }
    }

    private async void SaveScButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.SearchConsole.SaveAsync();
        ScClientSecretBox.Password = "";
    }

    private async void ConnectScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SearchConsole.ConnectAsync();

    private async void TestScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SearchConsole.TestAsync();

    private async void SyncScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SearchConsole.SyncAsync();

    private async void DeleteScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SearchConsole.DeleteAsync();

    private async void SavePerformanceButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.Performance.SaveAsync();
        CruxApiKeyBox.Password = "";
        PageSpeedApiKeyBox.Password = "";
    }

    private async void AddPerformanceUrlButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.Performance.AddUrlAsync();

    private async void DeletePerformanceUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PerformanceUrlInfo url)
        {
            await ViewModel.Performance.DeleteUrlAsync(url);
        }
    }

    private async void TestCruxButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.Performance.TestCruxAsync();

    private async void TestPageSpeedButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.Performance.TestPageSpeedAsync();

    private async void SyncPerformanceButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.Performance.SyncAsync();

    private async void DeletePerformanceButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.Performance.DeleteAsync();
        CruxApiKeyBox.Password = "";
        PageSpeedApiKeyBox.Password = "";
    }

    private async void SaveYouTubeButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.YouTube.SaveAsync();
        YouTubeClientSecretBox.Password = "";
    }

    private async void ConnectYouTubeButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.YouTube.ConnectAsync();

    private async void TestYouTubeButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.YouTube.TestAsync();

    private async void SyncYouTubeButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.YouTube.SyncAsync();

    private async void DeleteYouTubeButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.YouTube.DeleteAsync();
        YouTubeClientSecretBox.Password = "";
    }

    private async void SaveBingButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.Bing.SaveAsync();
        BingApiKeyBox.Password = "";
    }

    private async void AddBingSiteButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.Bing.AddSiteAsync();

    private async void DeleteBingSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string siteUrl)
        {
            await ViewModel.Bing.DeleteSiteAsync(siteUrl);
        }
    }

    private async void TestBingButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.Bing.TestAsync();

    private async void SyncBingButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.Bing.SyncAsync();

    private async void DeleteBingButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.Bing.DeleteAsync();
        BingApiKeyBox.Password = "";
    }

    private void SyncPasswordBoxesToViewModel()
    {
        ViewModel.Cloudflare.NewToken = CfTokenBox.Password;
        ViewModel.WebAnalytics.NewToken = WaTokenBox.Password;
        ViewModel.SearchConsole.NewClientSecret = ScClientSecretBox.Password;
        ViewModel.Performance.NewCruxApiKey = CruxApiKeyBox.Password;
        ViewModel.Performance.NewPageSpeedApiKey = PageSpeedApiKeyBox.Password;
        ViewModel.YouTube.NewClientSecret = YouTubeClientSecretBox.Password;
        ViewModel.Bing.NewApiKey = BingApiKeyBox.Password;
    }
}
