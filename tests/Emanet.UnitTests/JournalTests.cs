using Emanet.Core.Ledger;

namespace Emanet.UnitTests;

public sealed class JournalTests
{
    private static readonly Guid Booking = Guid.NewGuid();

    private static Dictionary<string, long> Lines(JournalEntry entry) => entry.Lines.ToDictionary(l => l.Account, l => l.Amount);

    [Fact]
    public void A_rent_charge_splits_between_the_owner_and_the_fee()
    {
        var entry = JournalEntry.RentCharged(Booking, Builders.Owner, 840_00, 84_00, Builders.Now);

        Assert.Equal(new Dictionary<string, long>
        {
            [Accounts.ProviderClearing] = 840_00,
            [Accounts.OwnerPayable(Builders.Owner)] = -756_00,
            [Accounts.PlatformFees] = -84_00,
        }, Lines(entry));
        Assert.Equal(($"booking:{Booking}:rent", JournalKind.RentCharged), (entry.Key, entry.Kind));
    }

    [Theory]
    [InlineData(840_00, 756_00, 84_00)]
    [InlineData(420_00, 378_00, 42_00)]
    [InlineData(333, 300, 33)]
    public void A_refund_comes_back_from_the_owner_and_the_fee_in_proportion(long refund, long fromOwner, long fromFee)
    {
        var entry = JournalEntry.RentRefunded(Booking, Builders.Owner, 840_00, 84_00, refund, Builders.Now);
        var lines = Lines(entry);

        Assert.Equal((-refund, fromOwner, fromFee), (lines[Accounts.ProviderClearing], lines[Accounts.OwnerPayable(Builders.Owner)], lines[Accounts.PlatformFees]));
        Assert.Equal(0, entry.Lines.Sum(l => l.Amount));
    }

    [Fact]
    public void An_entry_that_does_not_balance_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => JournalEntry.Post(JournalKind.Payout, null, "x", "x", Builders.Now, ("a", 100), ("b", -99)));
        Assert.Throws<InvalidOperationException>(() => JournalEntry.Post(JournalKind.Payout, null, "x", "x", Builders.Now, ("a", 0), ("b", 0)));
        Assert.Throws<InvalidOperationException>(() => JournalEntry.Post(JournalKind.Payout, null, "x", "x", Builders.Now, ("a", 0)));
    }
}
