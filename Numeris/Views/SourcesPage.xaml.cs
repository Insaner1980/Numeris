using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Models;
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
        await ViewModel.TestCloudflareAsync();
    }

    private async void SaveCfButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCloudflareAsync();
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
}
