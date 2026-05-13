using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Numeris.Controls;

public sealed partial class HorizontalBars : UserControl
{
    public HorizontalBars()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable<BarRow>), typeof(HorizontalBars),
            new PropertyMetadata(null, OnItemsChanged));

    public IEnumerable<BarRow>? Items
    {
        get => (IEnumerable<BarRow>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HorizontalBars bars)
        {
            bars.Render();
        }
    }

    private void Render()
    {
        ItemsHost.Items.Clear();
        if (Items is null)
        {
            RenderEmptyState();
            return;
        }

        long max = 0;
        var rows = new List<BarRow>(Items);
        if (rows.Count == 0)
        {
            RenderEmptyState();
            return;
        }

        foreach (var row in rows)
        {
            if (row.Value > max) max = row.Value;
        }
        if (max <= 0) max = 1;

        var accent = (Brush)Application.Current.Resources["NumerisAccentBrush"];
        var subdued = (Brush)Application.Current.Resources["HorizontalBarTrackBrush"];
        var secondaryText = (Brush)Application.Current.Resources["NumerisTextSecondaryBrush"];
        var tertiaryText = (Brush)Application.Current.Resources["NumerisTextTertiaryBrush"];

        foreach (var row in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetDouble("HorizontalBarLabelWidth", 140)) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetDouble("HorizontalBarValueWidth", 80)) });

            var label = new TextBlock
            {
                Text = row.Label,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13,
                Foreground = secondaryText,
            };
            Grid.SetColumn(label, 0);

            var trackContainer = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            var track = new Rectangle
            {
                Fill = subdued,
                RadiusX = 4,
                RadiusY = 4,
            };
            trackContainer.Children.Add(track);

            var fillWidth = (double)row.Value / max;
            var fill = new Rectangle
            {
                Fill = accent,
                RadiusX = 4,
                RadiusY = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            fill.SizeChanged += (s, e) => { /* width adjusted via parent SizeChanged below */ };
            trackContainer.SizeChanged += (s, e) =>
            {
                fill.Width = e.NewSize.Width * fillWidth;
            };
            trackContainer.Children.Add(fill);
            Grid.SetColumn(trackContainer, 1);

            var valueText = new TextBlock
            {
                Text = row.Value.ToString("N0", CultureInfo.CurrentCulture),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13,
                Foreground = tertiaryText,
            };
            Grid.SetColumn(valueText, 2);

            grid.Children.Add(label);
            grid.Children.Add(trackContainer);
            grid.Children.Add(valueText);
            ItemsHost.Items.Add(grid);
        }
    }

    private void RenderEmptyState()
    {
        var tertiaryText = (Brush)Application.Current.Resources["NumerisTextTertiaryBrush"];
        ItemsHost.Items.Add(new TextBlock
        {
            Text = "No data",
            FontSize = 13,
            Foreground = tertiaryText,
        });
    }

    private static double GetDouble(string key, double fallback)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is double number
            ? number
            : fallback;
    }
}

public sealed class BarRow
{
    public string Label { get; set; } = "";
    public long Value { get; set; }
}
