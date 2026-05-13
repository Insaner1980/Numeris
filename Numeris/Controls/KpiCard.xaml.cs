using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Numeris.Controls;

public sealed partial class KpiCard : UserControl
{
    public KpiCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(KpiCard),
            new PropertyMetadata("", OnMetricTextChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(KpiCard),
            new PropertyMetadata("", OnMetricTextChanged));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(KpiCard),
            new PropertyMetadata("", OnMetricTextChanged));

    public static readonly DependencyProperty ChangePctProperty =
        DependencyProperty.Register(nameof(ChangePct), typeof(double?), typeof(KpiCard),
            new PropertyMetadata(null, OnChangeChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public double? ChangePct
    {
        get => (double?)GetValue(ChangePctProperty);
        set => SetValue(ChangePctProperty, value);
    }

    public string ChangeText
    {
        get
        {
            if (ChangePct is not double pct) return "";
            var sign = pct > 0 ? "+" : "";
            return $"{sign}{pct.ToString("0.#", CultureInfo.InvariantCulture)}% vs previous period";
        }
    }

    public Brush ChangeBrush
    {
        get
        {
            if (ChangePct is not double pct) return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            return pct switch
            {
                > 0 => (Brush)Application.Current.Resources["SuccessBrush"],
                < 0 => (Brush)Application.Current.Resources["DangerBrush"],
                _ => (Brush)Application.Current.Resources["NumerisTextSecondaryBrush"],
            };
        }
    }

    public Visibility ShowChange => ChangePct.HasValue ? Visibility.Visible : Visibility.Collapsed;

    private static void OnMetricTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KpiCard card)
        {
            card.Bindings.Update();
        }
    }

    private static void OnChangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KpiCard card)
        {
            card.Bindings.Update();
        }
    }
}
