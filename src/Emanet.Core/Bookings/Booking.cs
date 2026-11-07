using Emanet.Core.Listings;
using Emanet.Core.Reviews;

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
    ClaimAccepted,
    ClaimRejected,
    ClaimSettled,
    DepositCharged,
    DepositReleased,
    Completed,
    Reviewed,
}

/// <summary>Where the renter stands on a damage claim.</summary>
public enum ClaimAnswer
{
    Accepted,
    Rejected,
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

    /// <summary>After the item comes back, the owner has this long to report damage; then the deposit is let go.</summary>
    public static readonly TimeSpan ClaimWithin = TimeSpan.FromHours(48);

    /// <summary>The renter has this long to answer a damage claim; silence accepts it.</summary>
    public static readonly TimeSpan AnswerClaimWithin = TimeSpan.FromHours(72);

    /// <summary>Wrong handover codes allowed before the code is replaced with a new one.</summary>
    public const int CodeTries = 5;

    public const int ClaimNoteMax = 1000;

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

    /// <summary>Shown on the renter's screen at pickup; the owner types it in to say the item was handed over.</summary>
    public string? PickupCode { get; private set; }

    /// <summary>Shown on the owner's screen at return; the renter types it in to say the item was given back.</summary>
    public string? ReturnCode { get; private set; }

    public int WrongCodes { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? ReturnedAt { get; private set; }

    /// <summary>What the owner says the damage cost, taken from the deposit if the renter agrees or an admin decides.</summary>
    public long ClaimMinor { get; private set; }

    public string ClaimNote { get; private set; } = string.Empty;

    public DateTimeOffset? ClaimedAt { get; private set; }

    public ClaimAnswer? RenterAnswer { get; private set; }

    public string RenterAnswerNote { get; private set; } = string.Empty;

    /// <summary>What was finally taken from the deposit for damage.</summary>
    public long DamageMinor { get; private set; }

    /// <summary>When the deposit was charged for damage or let go; null while that is still to be done.</summary>
    public DateTimeOffset? DepositSettledAt { get; private set; }

    public bool InvolvesUser(Guid userId) => OwnerId == userId || RenterId == userId;

    /// <summary>The last moment the owner can report damage.</summary>
    public DateTimeOffset? ClaimBy => ReturnedAt + ClaimWithin;

    /// <summary>The last moment the renter can answer a claim.</summary>
    public DateTimeOffset? AnswerBy => ClaimedAt + AnswerClaimWithin;

    public bool Holds => Status is BookingStatus.Confirmed or BookingStatus.Active or BookingStatus.Returned or BookingStatus.Disputed or BookingStatus.Completed;

    /// <summary>The renter sees the exact pickup point once the rental is theirs.</summary>
    public bool RevealsPlace => Status is BookingStatus.Confirmed or BookingStatus.Active;

    /// <summary>Both sides may review once the rental is over, for two weeks.</summary>
    public bool CanBeReviewed(DateTimeOffset now) => Status == BookingStatus.Completed && PickedUpAt is not null && now < ClosedAt + Review.Window;

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
        PickupCode = NewCode();
        ReturnCode = NewCode(PickupCode);
    }

    /// <summary>The owner hands the item over and types in the code from the renter's screen.</summary>
    public void PickUp(Guid by, string? code, DateTimeOffset now)
    {
        RequireOwner(by, "Only the owner confirms the handover; show them your code.");
        Expect(BookingStatus.Confirmed, "Only a confirmed rental can be picked up.");
        var today = Clock.Today(now);
        if (today < Start || today >= End)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"The pickup is from {Start:yyyy-MM-dd} until the day before {End:yyyy-MM-dd}.");
        }

        CheckCode(PickupCode!, code, () => PickupCode = NewCode(PickupCode));
        Status = BookingStatus.Active;
        PickedUpAt = now;
    }

    /// <summary>The renter gives the item back and types in the code from the owner's screen.</summary>
    public void GiveBack(Guid by, string? code, DateTimeOffset now)
    {
        if (by != RenterId)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the renter confirms the return; show them your code.");
        }

        Expect(BookingStatus.Active, "Only an item that was picked up can be given back.");
        CheckCode(ReturnCode!, code, () => ReturnCode = NewCode(ReturnCode));
        Status = BookingStatus.Returned;
        ReturnedAt = now;
    }

    /// <summary>The owner asks for part or all of the deposit for damage, within two days of the return.</summary>
    public void ReportDamage(Guid by, long amount, string? note, DateTimeOffset now)
    {
        RequireOwner(by, "Only the owner can report damage.");
        Expect(BookingStatus.Returned, "Damage is reported after the item comes back, before the deposit is let go.");
        if (now >= ClaimBy)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The time to report damage has passed.");
        }

        if (amount <= 0 || amount > DepositMinor)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A claim is more than nothing and at most the deposit ({DepositMinor / 100m:0.00} TL).");
        }

        var cleaned = Text.CleanParagraphs(note, ClaimNoteMax, "A description of the damage");
        if (cleaned.Length < 10)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Say what was damaged, in a sentence or two.");
        }

        Status = BookingStatus.Disputed;
        ClaimMinor = amount;
        ClaimNote = cleaned;
        ClaimedAt = now;
    }

    /// <summary>The owner says all is well before the claim window ends; the deposit is let go.</summary>
    public void AllGood(Guid by, DateTimeOffset now)
    {
        RequireOwner(by, "Only the owner can say the item came back fine.");
        Expect(BookingStatus.Returned, "Only a returned item can be signed off.");
        Complete(0, "The item came back fine.", now);
    }

    public void AcceptClaim(Guid by, DateTimeOffset now)
    {
        RequireRenter(by, "Only the renter answers a claim.");
        ExpectOpenClaim();
        RenterAnswer = ClaimAnswer.Accepted;
        Complete(ClaimMinor, "The renter accepted the claim.", now);
    }

    /// <summary>The renter disagrees; an administrator decides.</summary>
    public void RejectClaim(Guid by, string? note, DateTimeOffset now)
    {
        RequireRenter(by, "Only the renter answers a claim.");
        ExpectOpenClaim();
        if (now >= AnswerBy)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The time to answer has passed; the claim was accepted.");
        }

        RenterAnswer = ClaimAnswer.Rejected;
        RenterAnswerNote = Text.CleanParagraphs(note, ClaimNoteMax, "Your side");
    }

    /// <summary>An administrator settles a disputed claim at any amount from nothing to what was claimed.</summary>
    public void Resolve(long amount, string? note, DateTimeOffset now)
    {
        Expect(BookingStatus.Disputed, "Only a disputed rental can be resolved.");
        if (amount < 0 || amount > ClaimMinor)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"The decision is between nothing and the claim ({ClaimMinor / 100m:0.00} TL).");
        }

        Complete(amount, Text.CleanParagraphs(note, ReasonMax, "A note") is { Length: > 0 } cleaned ? cleaned : "Decided by an administrator.", now);
    }

    /// <summary>
    /// What the clock settles on its own: a quiet claim window lets the deposit go, a claim left unanswered is
    /// accepted, and a rental never picked up ends once its days are over. Returns whether anything changed.
    /// </summary>
    public bool Lapse(DateTimeOffset now)
    {
        switch (Status)
        {
            case BookingStatus.Returned when now >= ClaimBy:
                Complete(0, "No damage was reported.", now);
                return true;
            case BookingStatus.Disputed when RenterAnswer is null && now >= AnswerBy:
                RenterAnswer = ClaimAnswer.Accepted;
                Complete(ClaimMinor, "The renter did not answer the claim in time.", now);
                return true;
            case BookingStatus.Confirmed when Clock.Today(now) >= End:
                Complete(0, "The item was never picked up.", now);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The deposit still owes its settlement: a charge for damage, or being let go.</summary>
    public bool OwesDepositSettlement => Status == BookingStatus.Completed && DepositSettledAt is null;

    public void DepositSettled(DateTimeOffset now) => DepositSettledAt ??= now;

    private void Complete(long damage, string reason, DateTimeOffset now)
    {
        DamageMinor = damage;
        Close(BookingStatus.Completed, reason, now);
    }

    private void ExpectOpenClaim()
    {
        Expect(BookingStatus.Disputed, "There is no open claim.");
        if (RenterAnswer is not null)
        {
            throw new DomainException(ErrorCodes.InvalidState, "You already answered this claim.");
        }
    }

    private void CheckCode(string expected, string? given, Action renew)
    {
        if (string.Equals(expected, given?.Trim(), StringComparison.Ordinal))
        {
            WrongCodes = 0;
            return;
        }

        if (++WrongCodes >= CodeTries)
        {
            WrongCodes = 0;
            renew();
            throw new DomainException(ErrorCodes.WrongCode, "Too many wrong codes. A new code is on the other person's screen.");
        }

        throw new DomainException(ErrorCodes.WrongCode, $"That code is not right. {CodeTries - WrongCodes} tries left.");
    }

    private void RequireOwner(Guid by, string message)
    {
        if (by != OwnerId)
        {
            throw new DomainException(ErrorCodes.Forbidden, message);
        }
    }

    private void RequireRenter(Guid by, string message)
    {
        if (by != RenterId)
        {
            throw new DomainException(ErrorCodes.Forbidden, message);
        }
    }

    /// <summary>Six random digits, never the same as the code being replaced.</summary>
    private static string NewCode(string? replacing = null)
    {
        string code;
        do
        {
            code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }
        while (code == replacing);

        return code;
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
