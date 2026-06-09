using System;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Windows.UI;

namespace Numeris.Themes;

public static class ChartPalette
{
    public static SKColor Accent => FromResource("NumerisAccentColor");
    public static SKColor Secondary => FromResource("ChartSecondaryColor");
    public static SKColor Success => FromResource("SuccessColor");
    public static SKColor Warning => FromResource("WarningColor");
    public static SKColor Danger => FromResource("DangerColor");
    public static SKColor Info => FromResource("InfoColor");
    public static SKColor Muted => FromResource("ChartMutedColor");
    public static SKColor Panel => FromResource("ChartPanelColor");
    public static SKColor GridLine => FromResource("ChartGridLineColor");
    public static SKColor AxisText => FromResource("NumerisTextTertiaryColor");
    public static SKColor TooltipBackground => FromResource("CardSurfaceColor");
    public static SKColor BarNeutralTop => FromResource("ChartBarNeutralTopColor");
    public static SKColor BarNeutralMid => FromResource("ChartBarNeutralMidColor");
    public static SKColor BarNeutralBottom => FromResource("ChartBarNeutralBottomColor");
    public static SKColor BarHighlightTop => FromResource("ChartBarHighlightTopColor");
    public static SKColor BarHighlightMid => FromResource("ChartBarHighlightMidColor");
    public static SKColor BarHighlightBottom => FromResource("ChartBarHighlightBottomColor");
    public static SKColor BarCap => FromResource("ChartBarCapColor");
    public static SKColor ReferenceLine => FromResource("ChartReferenceLineColor");
    public static SKColor PositiveDelta => FromResource("PositiveDeltaColor");
    public static SKColor NegativeDelta => FromResource("NegativeDeltaColor");
    public static SKColor NeutralDelta => FromResource("NeutralDeltaColor");

    private static SKColor FromResource(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        throw new InvalidOperationException($"Missing color resource '{key}'.");
    }
}
