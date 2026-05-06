using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.ViewModels;
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

    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;
        InitializeComponent();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        var item = FindNavigationItem(_shell.LastPage);
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;
        if (!Routes.TryGetValue(tag, out var pageType)) return;
        _shell.LastPage = tag;
        ContentFrame.Navigate(pageType);
    }

    private NavigationViewItem? FindNavigationItem(string tag)
    {
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag as string == tag)
            {
                return navItem;
            }
        }
        foreach (var item in NavView.FooterMenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag as string == tag)
            {
                return navItem;
            }
        }
        return NavView.MenuItems.Count > 0 ? NavView.MenuItems[0] as NavigationViewItem : null;
    }
}
