using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    public double ChangeValue => ChangePct ?? 0;

    public string ChangeTooltip
    {
        get
        {
            if (ChangePct is not double pct) return "";
            var sign = pct > 0 ? "+" : "";
            return $"{sign}{pct.ToString("0.#", CultureInfo.InvariantCulture)}% vs previous period";
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
