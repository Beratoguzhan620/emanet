namespace Emanet.Core.Ledger;

public enum JournalKind
{
    /// <summary>The rent arrives from the renter's card: the owner's share is owed to them, the rest is the platform's.</summary>
    RentCharged,

    /// <summary>Rent goes back to the renter, taken from the owner's share and the fee in proportion.</summary>
    RentRefunded,

    /// <summary>Part of the deposit is taken for damage and owed to the owner.</summary>
    DamageCharged,

    /// <summary>Money paid out to an owner's bank account.</summary>
    Payout,
}

/// <summary>
/// The names of the books. Positive amounts are debits, negative are credits, and every entry sums to zero, which
/// the database checks when the transaction commits.
/// </summary>
public static class Accounts
{
    /// <summary>Money held at the payment provider for us (an asset: debit balance).</summary>
    public const string ProviderClearing = "provider:clearing";

    /// <summary>What the platform earned (income: credit balance).</summary>
    public const string PlatformFees = "platform:fees";

    /// <summary>Money sent out to owners' banks (a cash account: debits when paid).</summary>
    public const string PaidOut = "bank:payouts";

    /// <summary>What an owner is owed (a liability: credit balance).</summary>
    public static string OwnerPayable(Guid ownerId) => $"owner:{ownerId}:payable";
}

/// <summary>One balanced movement of money, recorded once: a second post with the same key is ignored.</summary>
public sealed class JournalEntry
{
    private readonly List<LedgerLine> _lines = [];

    private JournalEntry()
    {
    }

    public Guid Id { get; private set; }

    public JournalKind Kind { get; private set; }

    public Guid? BookingId { get; private set; }

    /// <summary>What made this entry, such as "booking:{id}:rent". Unique, so retries cannot post twice.</summary>
    public string Key { get; private set; } = string.Empty;

    public string Memo { get; private set; } = string.Empty;

    public DateTimeOffset PostedAt { get; private set; }

    public IReadOnlyList<LedgerLine> Lines => _lines;

    public static JournalEntry Post(JournalKind kind, Guid? bookingId, string key, string memo, DateTimeOffset now, params (string Account, long Amount)[] lines)
    {
        if (lines.Length < 2 || lines.Sum(l => l.Amount) != 0 || lines.Any(l => l.Amount == 0))
        {
            throw new InvalidOperationException($"A journal entry needs two or more non-zero lines that sum to zero; {key} does not.");
        }

        var entry = new JournalEntry { Id = Guid.CreateVersion7(now), Kind = kind, BookingId = bookingId, Key = key, Memo = memo, PostedAt = now };
        entry._lines.AddRange(lines.Select(l => LedgerLine.For(entry.Id, l.Account, l.Amount)));
        return entry;
    }

    /// <summary>The rent arriving: the provider holds it, the owner is owed their share, the platform keeps the fee.</summary>
    public static JournalEntry RentCharged(Guid bookingId, Guid ownerId, long rent, long fee, DateTimeOffset now) =>
        Post(JournalKind.RentCharged, bookingId, $"booking:{bookingId}:rent", "Rent charged", now,
            (Accounts.ProviderClearing, rent), (Accounts.OwnerPayable(ownerId), -(rent - fee)), (Accounts.PlatformFees, -fee));

    /// <summary>
    /// A refund, split between the owner's share and the fee in the proportion they were charged, so that a full
    /// refund undoes the charge exactly and a half refund halves both.
    /// </summary>
    public static JournalEntry RentRefunded(Guid bookingId, Guid ownerId, long rent, long fee, long refund, DateTimeOffset now)
    {
        var fromFee = (long)Math.Round(refund * (decimal)fee / rent, MidpointRounding.AwayFromZero);
        var lines = new List<(string, long)> { (Accounts.ProviderClearing, -refund), (Accounts.OwnerPayable(ownerId), refund - fromFee) };
        if (fromFee != 0)
        {
            lines.Add((Accounts.PlatformFees, fromFee));
        }

        return Post(JournalKind.RentRefunded, bookingId, $"booking:{bookingId}:refund", "Rent refunded", now, [.. lines]);
    }

    public static JournalEntry DamageCharged(Guid bookingId, Guid ownerId, long amount, DateTimeOffset now) =>
        Post(JournalKind.DamageCharged, bookingId, $"booking:{bookingId}:damage", "Damage taken from the deposit", now,
            (Accounts.ProviderClearing, amount), (Accounts.OwnerPayable(ownerId), -amount));
}

public sealed class LedgerLine
{
    private LedgerLine()
    {
    }

    public long Id { get; private set; }

    public Guid EntryId { get; private set; }

    public string Account { get; private set; } = string.Empty;

    /// <summary>Kuruş; positive debits, negative credits.</summary>
    public long Amount { get; private set; }

    internal static LedgerLine For(Guid entryId, string account, long amount) => new() { EntryId = entryId, Account = account, Amount = amount };
}
