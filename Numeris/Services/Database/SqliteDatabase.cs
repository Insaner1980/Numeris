using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Numeris.Helpers;

namespace Numeris.Services.Database;

public sealed class SqliteDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SqliteDatabase()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        Migrations.RunAll(_connection);
    }

    public async Task<T> ReadAsync<T>(Func<SqliteConnection, T> work, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(Action<SqliteConnection> work, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> WriteAsync<T>(Func<SqliteConnection, T> work, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return work(_connection);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasData()
    {
        _gate.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM cloudflare_traffic";
            var result = cmd.ExecuteScalar();
            return Convert.ToInt64(result) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void ClearAllData()
    {
        _gate.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM cloudflare_traffic;
                DELETE FROM cloudflare_countries;
                DELETE FROM cloudflare_status_codes;
                DELETE FROM search_console;
                DELETE FROM search_devices;
                DELETE FROM search_page_queries;
                DELETE FROM sitemap_urls;
                DELETE FROM play_installs;
                DELETE FROM play_ratings;
                DELETE FROM play_revenue;
                DELETE FROM play_crashes;
                DELETE FROM web_analytics_daily;
                DELETE FROM web_analytics_referrers;
                DELETE FROM web_analytics_pages;
                DELETE FROM web_analytics_countries;
                DELETE FROM web_analytics_sites;
                DELETE FROM uptime_checks;
                UPDATE connections SET status = 'mock', last_sync = NULL;
                """;
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
        _gate.Dispose();
    }
}
