using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Kartova.SharedKernel.Postgres;

internal sealed class PostgresAdvisoryLock(
    NpgsqlDataSource dataSource,
    ILogger<PostgresAdvisoryLock> logger) : IDistributedLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct)
    {
        var key = StableHash64(lockName);
        var conn = await dataSource.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@k)", conn);
            cmd.Parameters.AddWithValue("k", key);
            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (!acquired)
            {
                await conn.DisposeAsync();
                return null;
            }
            logger.LogDebug("Acquired advisory lock {LockName}", lockName);
            return new Handle(conn, key, lockName, logger);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    internal static long StableHash64(string input)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(input))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return (long)h;
        }
    }

    private sealed class Handle(NpgsqlConnection conn, long key, string name, ILogger log) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", conn);
                cmd.Parameters.AddWithValue("k", key);
                await cmd.ExecuteNonQueryAsync();
                log.LogDebug("Released advisory lock {LockName}", name);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Lock unlock failed for {LockName}", name);
            }
            finally
            {
                await conn.DisposeAsync();
            }
        }
    }
}
