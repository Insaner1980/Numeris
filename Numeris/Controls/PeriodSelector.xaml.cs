using System;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Numeris.Models;

namespace Numeris.Controls;

public sealed partial class PeriodSelector : UserControl
{
    public PeriodSelector()
    {
        InitializeComponent();
        ApplyLabels();
        UpdateSelectionVisuals();
    }

    public event EventHandler? SelectionChanged;

    public static readonly DependencyProperty SelectedPeriodProperty =
        DependencyProperty.Register(nameof(SelectedPeriod), typeof(Period), typeof(PeriodSelector),
            new PropertyMetadata(Period.Last7Days, OnSelectedPeriodChanged));

    public Period SelectedPeriod
    {
        get => (Period)GetValue(SelectedPeriodProperty);
        set => SetValue(SelectedPeriodProperty, value);
    }

    private void Last7Button_Click(object sender, RoutedEventArgs e) => SetSelectedPeriod(Period.Last7Days);
    private void Last30Button_Click(object sender, RoutedEventArgs e) => SetSelectedPeriod(Period.Last30Days);
    private void Last90Button_Click(object sender, RoutedEventArgs e) => SetSelectedPeriod(Period.Last90Days);
    private void AllButton_Click(object sender, RoutedEventArgs e) => SetSelectedPeriod(Period.All);

    private static void OnSelectedPeriodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PeriodSelector selector)
        {
            selector.UpdateSelectionVisuals();
        }
    }

    private void SetSelectedPeriod(Period period)
    {
        if (SelectedPeriod == period) return;
        SelectedPeriod = period;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyLabels()
    {
        foreach (var option in PeriodOptions.All)
        {
            var button = ButtonFor(option.Value);
            button.Content = option.Value.ShortLabel();
            ToolTipService.SetToolTip(button, option.Value.DisplayLabel());
            AutomationProperties.SetName(button, option.Value.DisplayLabel());
        }
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var option in PeriodOptions.All)
        {
            var button = ButtonFor(option.Value);
            var selected = option.Value == SelectedPeriod;
            button.Background = GetBrush(selected ? "PeriodSelectorSelectedBrush" : "TransparentLayerBrush");
            button.Foreground = GetBrush(selected ? "PeriodSelectorSelectedForegroundBrush" : "NumerisTextTertiaryBrush");
            button.BorderBrush = GetBrush(selected ? "PeriodSelectorSelectedBorderBrush" : "TransparentLayerBrush");
        }
    }

    private Button ButtonFor(Period period)
    {
        return period switch
        {
            Period.Last7Days => Last7Button,
            Period.Last30Days => Last30Button,
            Period.Last90Days => Last90Button,
            Period.All => AllButton,
            _ => Last7Button,
        };
    }

    private static Brush GetBrush(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
        {
            return brush;
        }

        throw new InvalidOperationException($"Missing brush resource '{key}'.");
    }
}
