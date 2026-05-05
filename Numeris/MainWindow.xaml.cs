using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.Views;

namespace Numeris;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Routes = new()
    {
        ["dashboard"] = typeof(DashboardPage),
        ["cloudflare"] = typeof(CloudflarePage),
        ["search"] = typeof(SearchConsolePage),
        ["health"] = typeof(HealthPage),
        ["sources"] = typeof(SourcesPage),
    };

    public MainWindow()
    {
        InitializeComponent();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        if (NavView.MenuItems.Count > 0 && NavView.MenuItems[0] is NavigationViewItem first)
        {
            NavView.SelectedItem = first;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;
        if (!Routes.TryGetValue(tag, out var pageType)) return;
        ContentFrame.Navigate(pageType);
    }
}
