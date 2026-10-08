using System;
using System.Globalization;
using Numeris.Models;

namespace Numeris.Services.Insights;

public static class Trend
{
    public static double ChangeRatio(MetricWindow metric)
    {
        if (!metric.HasCurrent || !metric.HasPrevious || !metric.HasComparison || metric.Previous == 0.0)
        {
            return 0.0;
        }

        return (metric.Current - metric.Previous) / metric.Previous;
    }

    public static TrendState Classify(
        MetricWindow metric,
        double flatTolerance = 0.05,
        double upThreshold = 0.25,
        double downThreshold = 0.10)
    {
        if (!metric.HasCurrent || !metric.HasPrevious || !metric.HasComparison
            || (metric.Current == 0.0 && metric.Previous == 0.0))
        {
            return TrendState.NoData;
        }

        if (metric.Previous == 0.0 && metric.Current > 0.0)
        {
            return TrendState.NewActivity;
        }

        // Remove binary floating-point noise at inclusive rule thresholds.
        var ratio = Math.Round(ChangeRatio(metric), 12);
        if (ratio >= upThreshold)
        {
            return TrendState.Up;
        }

        if (ratio <= -downThreshold)
        {
            return TrendState.Down;
        }

        return TrendState.Flat;
    }

    public static bool IsUp(MetricWindow metric, double threshold = 0.25)
        => Classify(metric, upThreshold: threshold) == TrendState.Up;

    public static bool IsFlat(MetricWindow metric, double tolerance = 0.05)
        => Classify(metric, flatTolerance: tolerance) == TrendState.Flat
           && Math.Abs(Math.Round(ChangeRatio(metric), 12)) <= tolerance;

    public static bool IsDown(MetricWindow metric, double threshold = 0.10)
        => Classify(metric, downThreshold: threshold) == TrendState.Down;

    public static string FormatRatio(double ratio)
    {
        var percent = Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        var text = percent.ToString("0", CultureInfo.InvariantCulture);
        return percent switch
        {
            > 0 => $"+{text}%",
            < 0 => $"{text}%",
            _ => "0%",
        };
    }
}

