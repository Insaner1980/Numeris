using System;
using System.Collections.Generic;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Numeris.ViewModels;
using Numeris.Views;
using Windows.UI;

namespace Numeris;

public sealed partial class MainWindow : Window
{
    private const string PrimaryTextColorResource = "NumerisTextPrimaryColor";

    private static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);

    private static readonly Dictionary<string, Type> Routes = new()
    {
        ["dashboard"] = typeof(DashboardPage),
        ["cloudflare"] = typeof(CloudflarePage),
        ["search"] = typeof(SearchConsolePage),
        ["bing"] = typeof(BingPage),
        ["performance"] = typeof(PerformancePage),
        ["health"] = typeof(HealthPage),
        ["sources"] = typeof(SourcesPage),
    };

    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell;
        InitializeComponent();
        ConfigureCustomTitleBar();
    }

    private void ConfigureCustomTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureTitleBarColors();
    }

    private void ConfigureTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = Transparent;
        titleBar.ForegroundColor = ReadColorResource(PrimaryTextColorResource);
        titleBar.InactiveBackgroundColor = Transparent;
        titleBar.InactiveForegroundColor = ReadColorResource("NumerisTextTertiaryColor");
        titleBar.ButtonBackgroundColor = Transparent;
        titleBar.ButtonForegroundColor = ReadColorResource(PrimaryTextColorResource);
        titleBar.ButtonHoverBackgroundColor = ReadColorResource("ControlSurfaceHoverColor");
        titleBar.ButtonHoverForegroundColor = ReadColorResource(PrimaryTextColorResource);
        titleBar.ButtonPressedBackgroundColor = ReadColorResource("ControlSurfacePressedColor");
        titleBar.ButtonPressedForegroundColor = ReadColorResource(PrimaryTextColorResource);
        titleBar.ButtonInactiveBackgroundColor = Transparent;
        titleBar.ButtonInactiveForegroundColor = ReadColorResource("NumerisTextTertiaryColor");
    }

    private static Color ReadColorResource(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : Transparent;
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
