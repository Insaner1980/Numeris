using System;
using Numeris.Models;

namespace Numeris.Services.Insights;

public static class Trend
{
    public static double ChangeRatio(MetricWindow metric)
    {
        if (metric.Previous == 0.0)
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
        if (metric.Current == 0.0 && metric.Previous == 0.0)
        {
            return TrendState.NoData;
        }

        if (metric.Previous == 0.0 && metric.Current > 0.0)
        {
            return TrendState.NewActivity;
        }

        var ratio = ChangeRatio(metric);
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
           && Math.Abs(ChangeRatio(metric)) <= tolerance;

    public static bool IsDown(MetricWindow metric, double threshold = 0.10)
        => Classify(metric, downThreshold: threshold) == TrendState.Down;

    public static string FormatRatio(double ratio)
    {
        var percent = (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        return percent switch
        {
            > 0 => $"+{percent}%",
            < 0 => $"{percent}%",
            _ => "0%",
        };
    }
}

