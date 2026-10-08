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
        ViewModel.Cloudflare.NewToken = CfTokenBox.Password;
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
        ViewModel.Cloudflare.NewToken = CfTokenBox.Password;
        await ViewModel.Cloudflare.SaveAsync();
        if (string.IsNullOrEmpty(ViewModel.Cloudflare.NewToken)) CfTokenBox.Password = "";
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
        ViewModel.WebAnalytics.NewToken = WaTokenBox.Password;
        await ViewModel.WebAnalytics.SaveAsync();
        if (string.IsNullOrEmpty(ViewModel.WebAnalytics.NewToken)) WaTokenBox.Password = "";
    }

    private async void DiscoverWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.WebAnalytics.DiscoverSitesAsync();

    private async void TestWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.WebAnalytics.TestAsync();

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
        ViewModel.SearchConsole.NewClientSecret = ScClientSecretBox.Password;
        await ViewModel.SearchConsole.SaveAsync();
        if (string.IsNullOrEmpty(ViewModel.SearchConsole.NewClientSecret)) ScClientSecretBox.Password = "";
    }

    private async void ConnectScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SearchConsole.ConnectAsync();

    private async void TestScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SearchConsole.TestAsync();

    private async void DeleteScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SearchConsole.DeleteAsync();

    private async void SavePerformanceButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Performance.NewCruxApiKey = CruxApiKeyBox.Password;
        ViewModel.Performance.NewPageSpeedApiKey = PageSpeedApiKeyBox.Password;
        await ViewModel.Performance.SaveAsync();
        if (string.IsNullOrEmpty(ViewModel.Performance.NewCruxApiKey)) CruxApiKeyBox.Password = "";
        if (string.IsNullOrEmpty(ViewModel.Performance.NewPageSpeedApiKey)) PageSpeedApiKeyBox.Password = "";
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

    private async void DeletePerformanceButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.Performance.DeleteAsync();
        CruxApiKeyBox.Password = "";
        PageSpeedApiKeyBox.Password = "";
    }

    private async void SaveBingButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Bing.NewApiKey = BingApiKeyBox.Password;
        await ViewModel.Bing.SaveAsync();
        if (string.IsNullOrEmpty(ViewModel.Bing.NewApiKey)) BingApiKeyBox.Password = "";
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

    private async void DeleteBingButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.Bing.DeleteAsync();
        BingApiKeyBox.Password = "";
    }

}
