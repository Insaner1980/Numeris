using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;
using Numeris.Models;

namespace Numeris.Services.Database.Repositories;

public sealed class SummaryRepository
{
    private readonly SqliteDatabase _db;

    public SummaryRepository(SqliteDatabase db) => _db = db;

    public Task<SummaryData> GetSummaryAsync(int days)
    {
        return _db.ReadAsync(connection =>
        {
            var endDate = DateOnly.FromDateTime(DateTime.Today);
            var startDate = endDate.AddDays(-days);
            var prevStart = startDate.AddDays(-days);

            string s(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var startStr = s(startDate);
            var endStr = s(endDate);
            var prevStartStr = s(prevStart);

            var visitorsTotal = ScalarLong(connection,
                "SELECT COALESCE(SUM(unique_visitors), 0) FROM cloudflare_traffic WHERE date >= $a AND date <= $b",
                ("$a", startStr), ("$b", endStr));
            var visitorsPrev = ScalarLong(connection,
                "SELECT COALESCE(SUM(unique_visitors), 0) FROM cloudflare_traffic WHERE date >= $a AND date < $b",
                ("$a", prevStartStr), ("$b", startStr));

            var clicksSql = """
                SELECT COALESCE(SUM(clicks), 0) FROM search_console
                WHERE (kind = 'daily' OR NOT EXISTS (SELECT 1 FROM search_console WHERE kind = 'daily'))
                  AND date >= $a AND date {0} $b
                """;
            var clicksTotal = ScalarLong(connection, string.Format(clicksSql, "<="),
                ("$a", startStr), ("$b", endStr));
            var clicksPrev = ScalarLong(connection, string.Format(clicksSql, "<"),
                ("$a", prevStartStr), ("$b", startStr));

            var installsTotal = ScalarLong(connection,
                "SELECT COALESCE(SUM(installs), 0) FROM play_installs WHERE date >= $a AND date <= $b",
                ("$a", startStr), ("$b", endStr));
            var installsPrev = ScalarLong(connection,
                "SELECT COALESCE(SUM(installs), 0) FROM play_installs WHERE date >= $a AND date < $b",
                ("$a", prevStartStr), ("$b", startStr));

            var revenueTotal = ScalarDouble(connection,
                "SELECT COALESCE(SUM(revenue), 0.0) FROM play_revenue WHERE date >= $a AND date <= $b",
                ("$a", startStr), ("$b", endStr));
            var revenuePrev = ScalarDouble(connection,
                "SELECT COALESCE(SUM(revenue), 0.0) FROM play_revenue WHERE date >= $a AND date < $b",
                ("$a", prevStartStr), ("$b", startStr));

            var avgRating = ScalarDouble(connection,
                $"SELECT COALESCE(average_rating, 0.0) FROM play_ratings WHERE package_name = '{Domains.PlayStorePackage}' ORDER BY date DESC LIMIT 1");
            var prevRating = ScalarDoubleOr(connection,
                $"SELECT COALESCE(average_rating, 0.0) FROM play_ratings WHERE package_name = '{Domains.PlayStorePackage}' AND date < $a ORDER BY date DESC LIMIT 1",
                avgRating,
                ("$a", startStr));

            var crashRate = ScalarDouble(connection,
                "SELECT COALESCE(AVG(crash_rate), 0.0) FROM play_crashes WHERE date >= $a AND date <= $b",
                ("$a", startStr), ("$b", endStr));
            var crashRatePrev = ScalarDouble(connection,
                "SELECT COALESCE(AVG(crash_rate), 0.0) FROM play_crashes WHERE date >= $a AND date < $b",
                ("$a", prevStartStr), ("$b", startStr));

            return new SummaryData
            {
                VisitorsTotal = visitorsTotal,
                VisitorsChangePct = PctChange(visitorsPrev, visitorsTotal),
                ClicksTotal = clicksTotal,
                ClicksChangePct = PctChange(clicksPrev, clicksTotal),
                InstallsTotal = installsTotal,
                InstallsChangePct = PctChange(installsPrev, installsTotal),
                RevenueTotal = Math.Round(revenueTotal * 100.0) / 100.0,
                RevenueChangePct = PctChange(revenuePrev, revenueTotal),
                AverageRating = Math.Round(avgRating * 10.0) / 10.0,
                RatingChange = Math.Round((avgRating - prevRating) * 10.0) / 10.0,
                CrashRate = Math.Round(crashRate * 100.0) / 100.0,
                CrashRateChange = Math.Round((crashRate - crashRatePrev) * 100.0) / 100.0,
            };
        });
    }

    private static long ScalarLong(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? 0 : Convert.ToInt64(result);
    }

    private static double ScalarDouble(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? 0.0 : Convert.ToDouble(result);
    }

    private static double ScalarDoubleOr(SqliteConnection connection, string sql, double fallback, params (string Name, object Value)[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? fallback : Convert.ToDouble(result);
    }

    private static double PctChange(double prev, double current)
    {
        if (prev == 0.0)
        {
            return current > 0.0 ? 100.0 : 0.0;
        }
        return Math.Round(((current - prev) / prev) * 1000.0) / 10.0;
    }
}
