using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Numeris.Controls;

public sealed partial class DeltaBadge : UserControl
{
    public DeltaBadge()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(DeltaBadge),
            new PropertyMetadata(0d, OnBadgePropertyChanged));

    public static readonly DependencyProperty InvertProperty =
        DependencyProperty.Register(nameof(Invert), typeof(bool), typeof(DeltaBadge),
            new PropertyMetadata(false, OnBadgePropertyChanged));

    public static readonly DependencyProperty SuffixProperty =
        DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(DeltaBadge),
            new PropertyMetadata("", OnBadgePropertyChanged));

    public static readonly DependencyProperty ShowSignProperty =
        DependencyProperty.Register(nameof(ShowSign), typeof(bool), typeof(DeltaBadge),
            new PropertyMetadata(true, OnBadgePropertyChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool Invert
    {
        get => (bool)GetValue(InvertProperty);
        set => SetValue(InvertProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public bool ShowSign
    {
        get => (bool)GetValue(ShowSignProperty);
        set => SetValue(ShowSignProperty, value);
    }

    public string DisplayText
    {
        get
        {
            var arrow = Value switch
            {
                > 0 => "\u25B2 ",
                < 0 => "\u25BC ",
                _ => "",
            };
            var sign = ShowSign && Value > 0 ? "+" : "";
            return $"{arrow}{sign}{Value.ToString("0.#", CultureInfo.InvariantCulture)}{Suffix}";
        }
    }

    public Brush DeltaBrush
    {
        get
        {
            var resourceKey = Value switch
            {
                > 0 => Invert ? "NegativeDeltaBrush" : "PositiveDeltaBrush",
                < 0 => Invert ? "PositiveDeltaBrush" : "NegativeDeltaBrush",
                _ => "NeutralDeltaBrush",
            };
            return GetBrush(resourceKey);
        }
    }

    private static void OnBadgePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DeltaBadge badge)
        {
            badge.Bindings.Update();
        }
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
