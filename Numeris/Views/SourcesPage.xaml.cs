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
        await ViewModel.TestCloudflareAsync();
    }

    private async void SaveCfButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.SaveCloudflareAsync();
        CfTokenBox.Password = "";
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CloudflareConnectionInfo info)
        {
            await ViewModel.SyncCloudflareAsync(info);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CloudflareConnectionInfo info)
        {
            await ViewModel.DeleteCloudflareAsync(info);
        }
    }

    private async void SaveWaButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.SaveWebAnalyticsAsync();
        WaTokenBox.Password = "";
    }

    private async void DiscoverWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.DiscoverWebAnalyticsSitesAsync();

    private async void SyncWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SyncWebAnalyticsAsync();

    private async void DeleteWaButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.DeleteWebAnalyticsAsync();

    private async void AddWaSiteButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.AddWebAnalyticsSiteAsync();

    private async void DeleteWaSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WebAnalyticsSite site)
        {
            await ViewModel.DeleteWebAnalyticsSiteAsync(site);
        }
    }

    private async void SaveScButton_Click(object sender, RoutedEventArgs e)
    {
        SyncPasswordBoxesToViewModel();
        await ViewModel.SaveSearchConsoleAsync();
        ScClientSecretBox.Password = "";
    }

    private async void ConnectScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.ConnectSearchConsoleAsync();

    private async void SyncScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SyncSearchConsoleAsync();

    private async void DeleteScButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.DeleteSearchConsoleAsync();

    private void SyncPasswordBoxesToViewModel()
    {
        ViewModel.NewCfToken = CfTokenBox.Password;
        ViewModel.NewWaToken = WaTokenBox.Password;
        ViewModel.NewScClientSecret = ScClientSecretBox.Password;
    }
}
