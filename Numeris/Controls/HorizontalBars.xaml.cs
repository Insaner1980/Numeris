using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using Windows.Foundation;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Numeris.Controls;

public sealed partial class HorizontalBars : UserControl
{
    private INotifyCollectionChanged? _itemsCollection;

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

    public static readonly DependencyProperty HighlightTopValueProperty =
        DependencyProperty.Register(nameof(HighlightTopValue), typeof(bool), typeof(HorizontalBars),
            new PropertyMetadata(true, OnAppearanceChanged));

    public bool HighlightTopValue
    {
        get => (bool)GetValue(HighlightTopValueProperty);
        set => SetValue(HighlightTopValueProperty, value);
    }

    public static readonly DependencyProperty CompactProperty =
        DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(HorizontalBars),
            new PropertyMetadata(false, OnAppearanceChanged));

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HorizontalBars bars)
        {
            if (bars._itemsCollection is not null)
            {
                bars._itemsCollection.CollectionChanged -= bars.OnItemsCollectionChanged;
            }

            bars._itemsCollection = e.NewValue as INotifyCollectionChanged;
            if (bars._itemsCollection is not null)
            {
                bars._itemsCollection.CollectionChanged += bars.OnItemsCollectionChanged;
            }

            bars.Render();
        }
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HorizontalBars bars)
        {
            bars.Render();
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Render();
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
        var scaleMax = max <= 0 ? 1 : max;

        var trackBrush = GetBrush("HorizontalBarTrackBrush");
        var secondaryText = GetBrush("NumerisTextSecondaryBrush");
        var tertiaryText = GetBrush("NumerisTextTertiaryBrush");
        var barHeight = GetDouble(Compact ? "HorizontalBarCompactHeight" : "HorizontalBarHeight", Compact ? 5 : 7);
        var barRadius = GetDouble(Compact ? "HorizontalBarCompactCornerRadius" : "HorizontalBarCornerRadius", Compact ? 2.5 : 3.5);
        var rowSpacing = GetDouble(Compact ? "HorizontalBarCompactRowSpacing" : "HorizontalBarRowSpacing", Compact ? 8 : 12);
        var fontSize = Compact ? 12 : 13;

        foreach (var row in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, rowSpacing) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetDouble("HorizontalBarLabelWidth", 140)) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GetDouble("HorizontalBarValueWidth", 80)) });

            var label = new TextBlock
            {
                Text = row.Label,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = fontSize,
                Foreground = secondaryText,
            };
            Grid.SetColumn(label, 0);

            var trackContainer = new Grid { Height = barHeight, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            var track = new Rectangle
            {
                Fill = trackBrush,
                RadiusX = barRadius,
                RadiusY = barRadius,
            };
            trackContainer.Children.Add(track);

            var fillWidth = Math.Clamp((double)Math.Max(0, row.Value) / scaleMax, 0, 1);
            var isHighlight = HighlightTopValue && row.Value == max && max > 0;
            var fill = new Rectangle
            {
                Fill = CreateBarFill(isHighlight),
                RadiusX = barRadius,
                RadiusY = barRadius,
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
                FontSize = fontSize,
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
        var tertiaryText = GetBrush("NumerisTextTertiaryBrush");
        ItemsHost.Items.Add(new TextBlock
        {
            Text = "No data",
            FontSize = 13,
            Foreground = tertiaryText,
        });
    }

    private static LinearGradientBrush CreateBarFill(bool isHighlight)
    {
        var top = GetColor(isHighlight ? "ChartBarHighlightTopColor" : "ChartBarNeutralTopColor");
        var mid = GetColor(isHighlight ? "ChartBarHighlightMidColor" : "ChartBarNeutralMidColor");
        var bottom = GetColor(isHighlight ? "ChartBarHighlightBottomColor" : "ChartBarNeutralBottomColor");
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };

        brush.GradientStops.Add(new GradientStop { Color = top, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = mid, Offset = 0.55 });
        brush.GradientStops.Add(new GradientStop { Color = bottom, Offset = 1 });
        return brush;
    }

    private static Brush GetBrush(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
        {
            return brush;
        }

        throw new InvalidOperationException($"Missing brush resource '{key}'.");
    }

    private static Color GetColor(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return color;
        }

        throw new InvalidOperationException($"Missing color resource '{key}'.");
    }

    private static double GetDouble(string key, double fallback)
    {
        return Application.Current?.Resources.TryGetValue(key, out var value) == true && value is double number
            ? number
            : fallback;
    }
}

public sealed class BarRow
{
    public string Label { get; set; } = "";
    public long Value { get; set; }
}
