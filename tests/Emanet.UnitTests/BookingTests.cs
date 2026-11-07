using Emanet.Core;
using Emanet.Core.Bookings;

namespace Emanet.UnitTests;

public sealed class BookingTests
{
    private static readonly Guid Renter = Guid.NewGuid();

    // 09:00 UTC is 12:00 in Turkey, so "today" is 7 November.
    private static readonly DateOnly Today = new(2026, 11, 7);

    private static Booking Request(int fromDay = 3, int days = 7, int minDays = 1, int maxDays = 30) =>
        Booking.Request(Builders.Drill(minDays: minDays, maxDays: maxDays), Renter, Today.AddDays(fromDay), Today.AddDays(fromDay + days), " Hafta sonu ", "pm_card_ok", Builders.Now);

    [Fact]
    public void A_request_prices_the_rent_takes_the_fee_and_waits_a_day_for_an_answer()
    {
        var booking = Request();

        Assert.Equal((7, 840_00L, 84_00L, 756_00L, 1000_00L), (booking.Days, booking.RentMinor, booking.FeeMinor, booking.OwnerShareMinor, booking.DepositMinor));
        Assert.Equal((BookingStatus.Requested, "Hafta sonu"), (booking.Status, booking.Message));
        Assert.Equal(Builders.Now.AddHours(24), booking.RespondBy);
        Assert.False(booking.Holds);
        Assert.False(booking.RevealsPlace);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, 0)]
    [InlineData(300, 100)]
    public void Dates_must_be_from_today_in_order_and_within_a_year(int fromDay, int days)
    {
        var ex = Assert.Throws<DomainException>(() => Request(fromDay, days));
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void A_rental_is_as_long_as_the_owner_allows(int days)
    {
        var ex = Assert.Throws<DomainException>(() => Request(days: days, minDays: 2, maxDays: 5));
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
    }

    [Fact]
    public void Owners_cannot_rent_their_own_things_nor_anyone_a_paused_listing()
    {
        var drill = Builders.Drill();
        var own = Assert.Throws<DomainException>(() => Booking.Request(drill, Builders.Owner, Today.AddDays(1), Today.AddDays(2), null, "pm_card_ok", Builders.Now));
        drill.Pause(Builders.Now);
        var paused = Assert.Throws<DomainException>(() => Booking.Request(drill, Renter, Today.AddDays(1), Today.AddDays(2), null, "pm_card_ok", Builders.Now));

        Assert.Equal((ErrorCodes.InvalidState, ErrorCodes.InvalidState), (own.Code, paused.Code));
    }

    [Fact]
    public void Confirming_needs_a_live_request()
    {
        var late = Request();
        var twice = Request();
        twice.Confirm(Builders.Now);

        Assert.Throws<DomainException>(() => late.Confirm(Builders.Now.AddHours(24)));
        Assert.Throws<DomainException>(() => twice.Confirm(Builders.Now));
        Assert.Throws<DomainException>(() => twice.Decline(null, Builders.Now));
        Assert.True(twice.Holds);
        Assert.True(twice.RevealsPlace);
        Assert.True(twice.OwesRentCharge);
    }

    [Fact]
    public void A_declined_or_lapsed_request_owes_its_holds_back_and_no_charge()
    {
        var declined = Request();
        declined.Decline("  ", Builders.Now);
        var lapsed = Request();
        lapsed.Expire(Builders.Now.AddHours(25));

        Assert.All([declined, lapsed], b => Assert.Equal((true, false, false), (b.OwesRelease, b.OwesRentCharge, b.OwesRefund)));
        declined.HoldsReleased(Builders.Now);
        Assert.False(declined.OwesRelease);
    }

    [Theory]
    // Pickup is 10 November 00:00 in Turkey, 9 November 21:00 UTC: 60 hours after the request.
    [InlineData(0, 840_00)]
    [InlineData(12, 840_00)]
    [InlineData(13, 420_00)]
    [InlineData(59, 420_00)]
    public void The_renter_gets_everything_back_two_days_ahead_and_half_after(int hoursLater, long refund)
    {
        var booking = Request();
        booking.Confirm(Builders.Now);

        Assert.Equal(refund, booking.Cancel(Renter, null, Builders.Now.AddHours(hoursLater)));
        Assert.Equal((BookingStatus.Cancelled, true, true), (booking.Status, booking.OwesRefund, booking.OwesRentCharge));
    }

    [Fact]
    public void The_owner_cancelling_refunds_everything_and_a_request_refunds_nothing()
    {
        var confirmed = Request();
        confirmed.Confirm(Builders.Now);
        var requested = Request();

        Assert.Equal(840_00, confirmed.Cancel(Builders.Owner, "Bozuldu", Builders.Now.AddHours(70)));
        Assert.Equal(0, requested.Cancel(Renter, null, Builders.Now));
        Assert.Equal((false, true), (requested.OwesRentCharge, requested.OwesRelease));
        Assert.Throws<DomainException>(() => requested.Cancel(Renter, null, Builders.Now));
    }

    [Theory]
    [InlineData(100_00, 10_00)]
    [InlineData(105, 11)]
    [InlineData(104, 10)]
    public void The_fee_is_ten_percent_to_the_nearest_kurus(long rent, long fee) => Assert.Equal(fee, Booking.FeeFor(rent));

    [Fact]
    public void Closed_days_follow_the_same_date_rules()
    {
        var closed = CalendarEntry.Block(Guid.NewGuid(), Today, Today.AddDays(2), " Bakımda ", Today, Builders.Now);

        Assert.Equal(("Bakımda", (Guid?)null), (closed.Note, closed.BookingId));
        Assert.Throws<DomainException>(() => CalendarEntry.Block(Guid.NewGuid(), Today.AddDays(-1), Today, null, Today, Builders.Now));
    }
}
