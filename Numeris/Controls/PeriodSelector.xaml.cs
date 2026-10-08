using System;
using System.Linq;
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
        foreach (var period in PeriodOptions.All.Select(option => option.Value))
        {
            var button = ButtonFor(period);
            button.Content = period.ShortLabel();
            ToolTipService.SetToolTip(button, period.DisplayLabel());
        }
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var period in PeriodOptions.All.Select(option => option.Value))
        {
            var button = ButtonFor(period);
            var selected = period == SelectedPeriod;
            AutomationProperties.SetName(button, period.DisplayLabel() + (selected ? ", selected" : ""));
            AutomationProperties.SetItemStatus(button, selected ? "Selected" : "Not selected");
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
