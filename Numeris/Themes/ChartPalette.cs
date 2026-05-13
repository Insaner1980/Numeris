using Microsoft.UI.Xaml;
using SkiaSharp;
using Windows.UI;

namespace Numeris.Themes;

public static class ChartPalette
{
    public static SKColor Accent => FromResource("NumerisAccentColor", new SKColor(217, 160, 82));
    public static SKColor Secondary => FromResource("ChartSecondaryColor", new SKColor(201, 123, 106));
    public static SKColor Success => FromResource("SuccessColor", new SKColor(130, 201, 143));
    public static SKColor Warning => FromResource("WarningColor", new SKColor(217, 160, 82));
    public static SKColor Danger => FromResource("DangerColor", new SKColor(224, 122, 122));
    public static SKColor Info => FromResource("InfoColor", new SKColor(143, 184, 232));
    public static SKColor Muted => FromResource("ChartMutedColor", new SKColor(236, 238, 242, 176));
    public static SKColor Panel => FromResource("ChartPanelColor", new SKColor(0, 0, 0, 41));
    public static SKColor GridLine => FromResource("ChartGridLineColor", new SKColor(255, 255, 255, 36));
    public static SKColor AxisText => FromResource("NumerisTextTertiaryColor", new SKColor(184, 184, 184));
    public static SKColor TooltipBackground => FromResource("CardSurfaceColor", new SKColor(255, 255, 255, 14));

    private static SKColor FromResource(string key, SKColor fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        return fallback;
    }
}
