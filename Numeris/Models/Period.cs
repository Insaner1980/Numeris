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

    public static DateRange ToDateRange(this Period period)
    {
        var end = DateOnly.FromDateTime(DateTime.Today);
        var days = period.Days();
        var start = end.AddDays(-(days - 1));
        return new DateRange(start, end, days);
    }
}
