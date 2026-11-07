using Emanet.Core;
using Emanet.Core.Bookings;
using Emanet.Core.Reviews;

namespace Emanet.UnitTests;

public sealed class HandoverTests
{
    private static readonly Guid Renter = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 7);
    private static readonly DateTimeOffset Now = Builders.Now;

    private static Booking Confirmed(int fromDay = 0, int days = 3)
    {
        var booking = Booking.Request(Builders.Drill(), Renter, Today.AddDays(fromDay), Today.AddDays(fromDay + days), null, "pm_card_ok", Now);
        booking.Confirm(Now);
        return booking;
    }

    private static Booking Returned()
    {
        var booking = Confirmed();
        booking.PickUp(Builders.Owner, booking.PickupCode, Now.AddHours(1));
        booking.GiveBack(Renter, booking.ReturnCode, Now.AddHours(50));
        return booking;
    }

    [Fact]
    public void Confirming_makes_two_different_six_digit_codes()
    {
        var booking = Confirmed();

        Assert.Matches("^[0-9]{6}$", booking.PickupCode);
        Assert.Matches("^[0-9]{6}$", booking.ReturnCode);
        Assert.NotEqual(booking.PickupCode, booking.ReturnCode);
    }

    [Fact]
    public void The_owner_takes_the_renters_code_on_a_rental_day_and_the_renter_the_owners()
    {
        var early = Confirmed(fromDay: 1);
        var booking = Confirmed();

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => early.PickUp(Builders.Owner, early.PickupCode, Now)).Code);
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => booking.PickUp(Renter, booking.PickupCode, Now)).Code);
        Assert.Equal(ErrorCodes.WrongCode, Assert.Throws<DomainException>(() => booking.PickUp(Builders.Owner, booking.ReturnCode, Now)).Code);
        booking.PickUp(Builders.Owner, $" {booking.PickupCode} ", Now);
        Assert.Equal((BookingStatus.Active, 0), (booking.Status, booking.WrongCodes));
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => booking.GiveBack(Builders.Owner, booking.ReturnCode, Now)).Code);
        booking.GiveBack(Renter, booking.ReturnCode, Now.AddHours(2));
        Assert.Equal((BookingStatus.Returned, Now.AddHours(50)), (booking.Status, booking.ClaimBy));
    }

    [Fact]
    public void Too_many_wrong_codes_replace_the_code()
    {
        var booking = Confirmed();
        var code = booking.PickupCode;

        for (var i = 0; i < Booking.CodeTries; i++)
        {
            Assert.Throws<DomainException>(() => booking.PickUp(Builders.Owner, "nope", Now));
        }

        Assert.NotEqual(code, booking.PickupCode);
        Assert.Equal(0, booking.WrongCodes);
        Assert.Throws<DomainException>(() => booking.PickUp(Builders.Owner, code, Now));
    }

    [Fact]
    public void Damage_is_claimed_within_two_days_for_up_to_the_deposit_with_a_description()
    {
        var late = Returned();
        var booking = Returned();
        var at = Now.AddHours(51);

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => late.ReportDamage(Builders.Owner, 100_00, "Uç kırık geldi, değişmeli.", Now.AddHours(98))).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => booking.ReportDamage(Builders.Owner, 1000_01, "Uç kırık geldi, değişmeli.", at)).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => booking.ReportDamage(Builders.Owner, 0, "Uç kırık geldi, değişmeli.", at)).Code);
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => booking.ReportDamage(Builders.Owner, 100_00, "Kırık", at)).Code);
        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => booking.ReportDamage(Renter, 100_00, "Uç kırık geldi, değişmeli.", at)).Code);
        booking.ReportDamage(Builders.Owner, 1000_00, "Uç kırık geldi, değişmeli.", at);
        Assert.Equal((BookingStatus.Disputed, at.AddHours(72)), (booking.Status, booking.AnswerBy));
    }

    [Fact]
    public void A_claim_is_accepted_or_rejected_once_and_an_administrator_decides_up_to_it()
    {
        var accepted = Returned();
        accepted.ReportDamage(Builders.Owner, 300_00, "Çanta yırtılmış, sapı kopmuş.", Now.AddHours(51));
        accepted.AcceptClaim(Renter, Now.AddHours(52));
        var rejected = Returned();
        rejected.ReportDamage(Builders.Owner, 300_00, "Çanta yırtılmış, sapı kopmuş.", Now.AddHours(51));
        rejected.RejectClaim(Renter, "Öyle teslim aldım.", Now.AddHours(52));

        Assert.Equal((BookingStatus.Completed, 300_00L, true), (accepted.Status, accepted.DamageMinor, accepted.OwesDepositSettlement));
        Assert.Throws<DomainException>(() => rejected.AcceptClaim(Renter, Now.AddHours(53)));
        Assert.Throws<DomainException>(() => rejected.Resolve(300_01, null, Now.AddHours(60)));
        rejected.Resolve(120_00, null, Now.AddHours(60));
        Assert.Equal((BookingStatus.Completed, 120_00L, "Decided by an administrator."), (rejected.Status, rejected.DamageMinor, rejected.Reason));
    }

    [Fact]
    public void The_clock_lets_the_deposit_go_accepts_silent_claims_and_ends_no_shows()
    {
        var quiet = Returned();
        var silent = Returned();
        silent.ReportDamage(Builders.Owner, 200_00, "Kablo ezilmiş, değişmeli.", Now.AddHours(51));
        var answered = Returned();
        answered.ReportDamage(Builders.Owner, 200_00, "Kablo ezilmiş, değişmeli.", Now.AddHours(51));
        answered.RejectClaim(Renter, null, Now.AddHours(52));
        var noShow = Confirmed();

        Assert.False(quiet.Lapse(Now.AddHours(97)));
        Assert.True(quiet.Lapse(Now.AddHours(98)));
        Assert.True(silent.Lapse(Now.AddHours(123)));
        Assert.False(answered.Lapse(Now.AddDays(30)));
        Assert.False(noShow.Lapse(Now.AddHours(50)));
        Assert.True(noShow.Lapse(Now.AddHours(64)));
        Assert.Equal((0L, 200_00L, 0L), (quiet.DamageMinor, silent.DamageMinor, noShow.DamageMinor));
        Assert.All([quiet, silent, noShow], b => Assert.Equal(BookingStatus.Completed, b.Status));
    }

    [Fact]
    public void Reviews_come_after_a_rental_that_happened_for_two_weeks_with_one_to_five_stars()
    {
        var done = Returned();
        done.AllGood(Builders.Owner, Now.AddHours(51));
        var noShow = Confirmed();
        noShow.Lapse(Now.AddHours(64));

        var byRenter = Review.Write(done, Renter, 5, " Çok iyi ", Now.AddDays(3));
        var byOwner = Review.Write(done, Builders.Owner, 4, null, Now.AddDays(3));

        Assert.Equal((ReviewSubject.Listing, Builders.Owner, "Çok iyi"), (byRenter.Subject, byRenter.SubjectUserId, byRenter.Comment));
        Assert.Equal((ReviewSubject.Renter, Renter), (byOwner.Subject, byOwner.SubjectUserId));
        Assert.Throws<DomainException>(() => Review.Write(done, Renter, 6, null, Now.AddDays(3)));
        Assert.Throws<DomainException>(() => Review.Write(done, Renter, 5, null, Now.AddDays(17)));
        Assert.Throws<DomainException>(() => Review.Write(noShow, Renter, 5, null, Now.AddDays(3)));
        Assert.Throws<DomainException>(() => Review.Write(done, Guid.NewGuid(), 5, null, Now.AddDays(3)));
    }
}
