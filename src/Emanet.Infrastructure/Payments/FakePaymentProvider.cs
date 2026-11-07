using System.Security.Cryptography;
using Emanet.Core.Payments;
using Npgsql;

namespace Emanet.Infrastructure.Payments;

/// <summary>
/// A stand-in for a card payment provider, kept in its own schema as if it were someone else's system. It behaves
/// like the real ones where it matters: amounts are held, captured, released and refunded; every call is keyed and
/// a repeated key returns the first answer; amounts never go past what was held or taken. Three test payment methods
/// decide what the "card" does.
/// </summary>
public sealed class FakePaymentProvider(NpgsqlDataSource dataSource, TimeProvider clock) : IPaymentProvider
{
    public const string Approved = "pm_card_ok";
    public const string Declined = "pm_card_declined";
    public const string NoFunds = "pm_card_no_funds";

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            CREATE SCHEMA IF NOT EXISTS fakepay;
            CREATE TABLE IF NOT EXISTS fakepay.holds (
                id text PRIMARY KEY,
                amount bigint NOT NULL CHECK (amount > 0),
                captured bigint NOT NULL DEFAULT 0,
                refunded bigint NOT NULL DEFAULT 0,
                status text NOT NULL,
                method text NOT NULL,
                created_at timestamptz NOT NULL,
                CHECK (captured BETWEEN 0 AND amount),
                CHECK (refunded BETWEEN 0 AND captured));
            CREATE TABLE IF NOT EXISTS fakepay.calls (
                key text PRIMARY KEY,
                hold_id text NOT NULL,
                kind text NOT NULL,
                amount bigint NOT NULL,
                at timestamptz NOT NULL);
            """);
        // Several API instances may start together; creating the same objects at once can collide, so retry once.
        try
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DuplicateObject or PostgresErrorCodes.DuplicateTable)
        {
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<Hold> HoldAsync(long amount, string paymentMethod, string key, CancellationToken ct)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await PreviousAsync(connection, key, ct) is { } previous)
        {
            return await GetAsync(connection, previous, ct);
        }

        switch (paymentMethod)
        {
            case Approved:
                break;
            case Declined:
                throw new PaymentDeclinedException("The card was declined.");
            case NoFunds:
                throw new PaymentDeclinedException("The card does not have enough funds.");
            default:
                throw new PaymentDeclinedException("This payment method is not known.");
        }

        var id = "hold_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        await ExecuteAsync(connection, "INSERT INTO fakepay.holds (id, amount, status, method, created_at) VALUES (@id, @amount, 'Held', @method, @at)",
            ct, ("id", id), ("amount", amount), ("method", paymentMethod), ("at", clock.GetUtcNow()));
        await RememberAsync(connection, key, id, "hold", amount, ct);
        await transaction.CommitAsync(ct);
        return new Hold(id, amount, 0, 0, HoldStatus.Held);
    }

    public Task<Hold> CaptureAsync(string holdId, long amount, string key, CancellationToken ct) =>
        ChangeAsync(holdId, key, "capture", amount, (hold) =>
        {
            if (hold.Status != HoldStatus.Held || amount <= 0 || hold.Captured + amount > hold.Amount)
            {
                throw new InvalidOperationException($"Cannot capture {amount} on {holdId}: {hold}.");
            }

            return ("UPDATE fakepay.holds SET captured = captured + @amount, status = 'Captured' WHERE id = @id", amount);
        }, ct);

    public Task<Hold> ReleaseAsync(string holdId, string key, CancellationToken ct) =>
        ChangeAsync(holdId, key, "release", 0, hold => hold.Status == HoldStatus.Held && hold.Captured == 0
            ? ("UPDATE fakepay.holds SET status = 'Released' WHERE id = @id", 0)
            : (null, 0), ct);

    public Task<Hold> RefundAsync(string holdId, long amount, string key, CancellationToken ct) =>
        ChangeAsync(holdId, key, "refund", amount, hold =>
        {
            if (amount <= 0 || hold.Refunded + amount > hold.Captured)
            {
                throw new InvalidOperationException($"Cannot refund {amount} on {holdId}: {hold}.");
            }

            return ("UPDATE fakepay.holds SET refunded = refunded + @amount WHERE id = @id", amount);
        }, ct);

    public async Task<long> NetCapturedAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT coalesce(sum(captured - refunded), 0)::bigint FROM fakepay.holds");
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>One keyed change to a hold, under a row lock, recorded so a retry gets the same answer.</summary>
    private async Task<Hold> ChangeAsync(string holdId, string key, string kind, long amount, Func<Hold, (string? Sql, long Amount)> plan, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await PreviousAsync(connection, key, ct) is not null)
        {
            return await GetAsync(connection, holdId, ct);
        }

        var hold = await GetAsync(connection, holdId, ct, forUpdate: true);
        var (sql, moved) = plan(hold);
        if (sql is not null)
        {
            await ExecuteAsync(connection, sql, ct, ("id", holdId), ("amount", moved));
        }

        await RememberAsync(connection, key, holdId, kind, amount, ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(connection, holdId, ct);
    }

    private static async Task<string?> PreviousAsync(NpgsqlConnection connection, string key, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT hold_id FROM fakepay.calls WHERE key = @key", connection);
        command.Parameters.AddWithValue("key", key);
        return (string?)await command.ExecuteScalarAsync(ct);
    }

    private async Task RememberAsync(NpgsqlConnection connection, string key, string holdId, string kind, long amount, CancellationToken ct) =>
        await ExecuteAsync(connection, "INSERT INTO fakepay.calls (key, hold_id, kind, amount, at) VALUES (@key, @hold, @kind, @amount, @at)",
            ct, ("key", key), ("hold", holdId), ("kind", kind), ("amount", amount), ("at", clock.GetUtcNow()));

    private static async Task<Hold> GetAsync(NpgsqlConnection connection, string holdId, CancellationToken ct, bool forUpdate = false)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT id, amount, captured, refunded, status FROM fakepay.holds WHERE id = @id{(forUpdate ? " FOR UPDATE" : string.Empty)}", connection);
        command.Parameters.AddWithValue("id", holdId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException($"There is no hold {holdId}.");
        }

        return new Hold(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), Enum.Parse<HoldStatus>(reader.GetString(4)));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}
