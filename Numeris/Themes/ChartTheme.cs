using System.Collections.Generic;
using System.Linq;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Numeris.Themes;

public static class ChartTheme
{
    public static CartesianChart CreateCartesianChart()
    {
        return new CartesianChart
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            LegendPosition = LegendPosition.Bottom,
            LegendTextPaint = new SolidColorPaint(ChartPalette.AxisText),
            LegendTextSize = 13,
            TooltipPosition = TooltipPosition.Top,
            TooltipTextPaint = new SolidColorPaint(ChartPalette.AxisText),
            TooltipBackgroundPaint = new SolidColorPaint(ChartPalette.TooltipBackground),
            TooltipTextSize = 13,
        };
    }

    public static Grid CreateChartSurface(CartesianChart chart)
    {
        return new Grid
        {
            Children =
            {
                CreateMeshBackdrop(),
                chart,
            },
        };
    }

    public static Axis StyleXAxis(Axis axis)
    {
        return StyleAxis(axis, showSeparators: false);
    }

    public static Axis StyleYAxis(Axis axis)
    {
        return StyleAxis(axis, showSeparators: true);
    }

    private static Axis StyleAxis(Axis axis, bool showSeparators)
    {
        axis.TextSize = 13;
        axis.LabelsPaint = new SolidColorPaint(ChartPalette.AxisText);
        axis.SeparatorsPaint = showSeparators
            ? new SolidColorPaint(ChartPalette.GridLine) { StrokeThickness = 1 }
            : null;
        return axis;
    }

    private static Viewbox CreateMeshBackdrop()
    {
        var lineBrush = GetBrush("ChartMeshLineBrush");
        var pointBrush = GetBrush("ChartMeshPointBrush");
        var canvas = new Canvas
        {
            Width = 1000,
            Height = 320,
            IsHitTestVisible = false,
            Opacity = GetDouble("ChartMeshOpacity", 0.16),
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var rows = new[]
        {
            new[] { new Point(0, 245), new Point(130, 214), new Point(275, 236), new Point(430, 198), new Point(590, 224), new Point(760, 184), new Point(1000, 214) },
            new[] { new Point(0, 275), new Point(135, 246), new Point(280, 268), new Point(440, 232), new Point(600, 258), new Point(765, 218), new Point(1000, 250) },
            new[] { new Point(0, 305), new Point(140, 284), new Point(290, 302), new Point(455, 270), new Point(620, 292), new Point(780, 260), new Point(1000, 288) },
        };

        foreach (var row in rows)
        {
            canvas.Children.Add(new Polyline
            {
                Points = ToPointCollection(row),
                Stroke = lineBrush,
                StrokeThickness = 1,
            });
        }

        for (var rowIndex = 0; rowIndex < rows.Length - 1; rowIndex++)
        {
            for (var pointIndex = 0; pointIndex < rows[rowIndex].Length; pointIndex++)
            {
                canvas.Children.Add(new Polyline
                {
                    Points = ToPointCollection(new[] { rows[rowIndex][pointIndex], rows[rowIndex + 1][pointIndex] }),
                    Stroke = lineBrush,
                    StrokeThickness = 1,
                });

                if (pointIndex < rows[rowIndex].Length - 1)
                {
                    canvas.Children.Add(new Polyline
                    {
                        Points = ToPointCollection(new[] { rows[rowIndex][pointIndex], rows[rowIndex + 1][pointIndex + 1] }),
                        Stroke = lineBrush,
                        StrokeThickness = 0.8,
                    });
                }
            }
        }

        foreach (var point in rows.SelectMany(row => row))
        {
            var dot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = pointBrush,
            };
            Canvas.SetLeft(dot, point.X - 1.5);
            Canvas.SetTop(dot, point.Y - 1.5);
            canvas.Children.Add(dot);
        }

        return new Viewbox
        {
            Stretch = Stretch.Fill,
            Child = canvas,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static PointCollection ToPointCollection(IEnumerable<Point> points)
    {
        var collection = new PointCollection();
        foreach (var point in points)
        {
            collection.Add(point);
        }
        return collection;
    }

    private static Brush GetBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Windows.UI.Color.FromArgb(48, 217, 160, 82));
    }

    private static double GetDouble(string key, double fallback)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is double number
            ? number
            : fallback;
    }
}
