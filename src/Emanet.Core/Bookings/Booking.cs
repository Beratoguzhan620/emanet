using Emanet.Core.Listings;

namespace Emanet.Core.Bookings;

public enum BookingStatus
{
    /// <summary>Waiting for the owner; the renter's card holds the rent and the deposit.</summary>
    Requested = 0,

    /// <summary>The dates are the renter's and the rent is charged; the deposit stays on hold.</summary>
    Confirmed = 1,

    Declined = 2,

    /// <summary>The owner did not answer in time.</summary>
    Expired = 3,

    Cancelled = 4,

    /// <summary>The renter has the item.</summary>
    Active = 5,

    /// <summary>The item is back; the deposit is released unless the owner reports damage.</summary>
    Returned = 6,

    /// <summary>The owner reported damage; the deposit waits for the claim to be settled.</summary>
    Disputed = 7,

    /// <summary>All settled: nothing more happens to this rental.</summary>
    Completed = 8,
}

public enum BookingEventKind
{
    Requested,
    Confirmed,
    Declined,
    Expired,
    Cancelled,
    Refunded,
    RentCharged,
    HoldsReleased,
    PickedUp,
    Returned,
    DamageReported,
    ClaimSettled,
    DepositReleased,
    Completed,
}

/// <summary>
/// One rental, from request to settlement. Prices are copied from the listing when it is requested, so a later price
/// change never touches a rental already agreed.
/// </summary>
public sealed class Booking
{
    public const int MessageMax = 500;
    public const int ReasonMax = 300;
    public const int MaxDaysAhead = 365;

    /// <summary>The platform keeps this share of the rent; the owner gets the rest.</summary>
    public const int FeePercent = 10;

    /// <summary>An owner has this long to answer a request before it lapses and the holds are released.</summary>
    public static readonly TimeSpan AnswerWithin = TimeSpan.FromHours(24);

    /// <summary>Cancelling this long before the pickup day refunds the whole rent; later, half.</summary>
    public static readonly TimeSpan FreeCancellation = TimeSpan.FromHours(48);

    private Booking()
    {
    }

    public Guid Id { get; private set; }

    public Guid ListingId { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid RenterId { get; private set; }

    public DateOnly Start { get; private set; }

    public DateOnly End { get; private set; }

    public int Days { get; private set; }

    public long RentMinor { get; private set; }

    public long FeeMinor { get; private set; }

    public long DepositMinor { get; private set; }

    public BookingStatus Status { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    /// <summary>The payment method token the renter chose; never a card number.</summary>
    public string PaymentMethod { get; private set; } = string.Empty;

    /// <summary>The provider's hold for the rent.</summary>
    public string? RentHoldId { get; private set; }

    /// <summary>The provider's hold for the deposit.</summary>
    public string? DepositHoldId { get; private set; }

    public long RefundMinor { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset RespondBy { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>When the rent was charged; null while the charge is still to be made.</summary>
    public DateTimeOffset? RentChargedAt { get; private set; }

    /// <summary>When the holds were let go after a request that came to nothing; null while that is still to be done.</summary>
    public DateTimeOffset? HoldsReleasedAt { get; private set; }

    /// <summary>When a refund owed after a cancellation went back to the card.</summary>
    public DateTimeOffset? RefundedAt { get; private set; }

    public bool InvolvesUser(Guid userId) => OwnerId == userId || RenterId == userId;

    public bool Holds => Status is BookingStatus.Confirmed or BookingStatus.Active or BookingStatus.Returned or BookingStatus.Disputed or BookingStatus.Completed;

    /// <summary>The renter sees the exact pickup point once the rental is theirs.</summary>
    public bool RevealsPlace => Status is BookingStatus.Confirmed or BookingStatus.Active;

    public long OwnerShareMinor => RentMinor - FeeMinor;

    public static Booking Request(Listing listing, Guid renterId, DateOnly start, DateOnly end, string? message, string paymentMethod, DateTimeOffset now)
    {
        if (listing.Status != ListingStatus.Active)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This listing is not taking requests right now.");
        }

        if (listing.IsOwnedBy(renterId))
        {
            throw new DomainException(ErrorCodes.InvalidState, "You cannot rent your own listing.");
        }

        CheckDates(start, end, Clock.Today(now));
        var days = end.DayNumber - start.DayNumber;
        var rent = listing.RentFor(days);
        return new Booking
        {
            Id = Guid.CreateVersion7(now),
            ListingId = listing.Id,
            OwnerId = listing.OwnerId,
            RenterId = renterId,
            Start = start,
            End = end,
            Days = days,
            RentMinor = rent,
            FeeMinor = FeeFor(rent),
            DepositMinor = listing.DepositMinor,
            Status = BookingStatus.Requested,
            Message = Text.CleanParagraphs(message, MessageMax, "A message"),
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? throw new DomainException(ErrorCodes.ValidationFailed, "Choose how to pay.") : paymentMethod.Trim(),
            RequestedAt = now,
            RespondBy = now + AnswerWithin,
        };
    }

    public static long FeeFor(long rent) => (long)Math.Round(rent * FeePercent / 100m, MidpointRounding.AwayFromZero);

    /// <summary>Pickup today or later, return after pickup, both within a year.</summary>
    public static void CheckDates(DateOnly start, DateOnly end, DateOnly today)
    {
        if (start < today)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The pickup day has passed.");
        }

        if (end <= start)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The return day comes after the pickup day.");
        }

        if (end > today.AddDays(MaxDaysAhead))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Book at most {MaxDaysAhead} days ahead.");
        }
    }

    public void Held(string rentHoldId, string? depositHoldId)
    {
        RentHoldId = rentHoldId;
        DepositHoldId = depositHoldId;
    }

    public void Confirm(DateTimeOffset now)
    {
        Expect(BookingStatus.Requested, "Only a request can be accepted.");
        if (now >= RespondBy)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This request ran out before it was accepted.");
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
    }

    public void Decline(string? reason, DateTimeOffset now)
    {
        Expect(BookingStatus.Requested, "Only a request can be declined.");
        Close(BookingStatus.Declined, Text.CleanParagraphs(reason, ReasonMax, "A reason"), now);
    }

    public void Expire(DateTimeOffset now)
    {
        Expect(BookingStatus.Requested, "Only a request can lapse.");
        Close(BookingStatus.Expired, "The owner did not answer in time.", now);
    }

    /// <summary>
    /// Cancels a request or a confirmed rental. Returns the rent to give back: all of it when the owner cancels or
    /// when there is still time, half of it once the pickup is less than two days away. Nothing was charged for a
    /// request, so nothing is refunded; its holds are released instead.
    /// </summary>
    public long Cancel(Guid by, string? reason, DateTimeOffset now)
    {
        if (Status is not (BookingStatus.Requested or BookingStatus.Confirmed))
        {
            throw new DomainException(ErrorCodes.InvalidState, Status == BookingStatus.Active
                ? "The item has been picked up; the rental ends when it comes back."
                : "This rental can no longer be cancelled.");
        }

        var wasConfirmed = Status == BookingStatus.Confirmed;
        var byOwner = by == OwnerId;
        Close(BookingStatus.Cancelled, Text.CleanParagraphs(reason, ReasonMax, "A reason"), now);
        if (!wasConfirmed)
        {
            return 0;
        }

        var early = Clock.StartOf(Start) - now >= FreeCancellation;
        RefundMinor = byOwner || early ? RentMinor : RentMinor / 2;
        return RefundMinor;
    }

    public void RentCharged(DateTimeOffset now) => RentChargedAt ??= now;

    public void HoldsReleased(DateTimeOffset now) => HoldsReleasedAt ??= now;

    public void Refunded(DateTimeOffset now) => RefundedAt ??= now;

    /// <summary>A rental that was confirmed and then cancelled still owes its rent charge (so it can be refunded).</summary>
    public bool OwesRentCharge => RentChargedAt is null && (Holds || (Status == BookingStatus.Cancelled && ConfirmedAt is not null));

    public bool OwesRefund => RefundMinor > 0 && RefundedAt is null;

    /// <summary>Holds to let go: after a request that came to nothing, or the deposit hold after a cancelled rental.</summary>
    public bool OwesRelease => HoldsReleasedAt is null && Status is BookingStatus.Declined or BookingStatus.Expired or BookingStatus.Cancelled;

    private void Close(BookingStatus status, string reason, DateTimeOffset now)
    {
        Status = status;
        Reason = reason;
        ClosedAt = now;
    }

    private void Expect(BookingStatus status, string message)
    {
        if (Status != status)
        {
            throw new DomainException(ErrorCodes.InvalidState, message);
        }
    }
}

/// <summary>One line of a rental's history.</summary>
public sealed class BookingEvent
{
    private BookingEvent()
    {
    }

    public long Id { get; private set; }

    public Guid BookingId { get; private set; }

    public BookingEventKind Kind { get; private set; }

    public Guid? ActorId { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    public static BookingEvent Record(Booking booking, BookingEventKind kind, Guid? actorId, DateTimeOffset at, string detail = "") =>
        new() { BookingId = booking.Id, Kind = kind, ActorId = actorId, At = at, Detail = detail };
}
