using System;

namespace Numeris.Models;

public enum Period
{
    Last7Days,
    Last30Days,
    Last90Days,
    All
}

public readonly record struct DateRange(DateOnly Start, DateOnly End, int Days);

public static class PeriodExtensions
{
    public static int Days(this Period period) => period switch
    {
        Period.Last7Days => 7,
        Period.Last30Days => 30,
        Period.Last90Days => 90,
        Period.All => 365,
        _ => 7
    };

    public static string ShortLabel(this Period period) => period switch
    {
        Period.Last7Days => "7d",
        Period.Last30Days => "30d",
        Period.Last90Days => "90d",
        Period.All => "All",
        _ => "7d"
    };

    public static string DisplayLabel(this Period period) => period switch
    {
        Period.Last7Days => "Last 7 days",
        Period.Last30Days => "Last 30 days",
        Period.Last90Days => "Last 90 days",
        Period.All => "All time",
        _ => "Last 7 days"
    };

    public static DateRange ToDateRange(this Period period)
    {
        var end = DateOnly.FromDateTime(DateTime.Today);
        var days = period.Days();
        var start = end.AddDays(-(days - 1));
        return new DateRange(start, end, days);
    }
}

public sealed record PeriodOption(Period Value, string Label);

public static class PeriodOptions
{
    public static readonly PeriodOption[] All =
    {
        new(Period.Last7Days, Period.Last7Days.ShortLabel()),
        new(Period.Last30Days, Period.Last30Days.ShortLabel()),
        new(Period.Last90Days, Period.Last90Days.ShortLabel()),
        new(Period.All, Period.All.ShortLabel()),
    };
}
