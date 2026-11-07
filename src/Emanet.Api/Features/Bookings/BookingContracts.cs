using Emanet.Api.Features.Listings;
using Emanet.Core.Bookings;

namespace Emanet.Api.Features.Bookings;

/// <summary>
/// A request to rent. <c>paymentMethod</c> is a token from the payment provider; with the stand-in provider,
/// <c>pm_card_ok</c> pays, <c>pm_card_declined</c> is refused and <c>pm_card_no_funds</c> has too little money.
/// </summary>
public sealed record BookingRequest(Guid ListingId, DateOnly Start, DateOnly End, string? Message, string PaymentMethod);

public sealed record ReasonRequest(string? Reason);

public sealed record BlockRequest(DateOnly Start, DateOnly End, string? Note);

/// <summary>One side of a rental. The rating is what owners gave this person as a renter.</summary>
public sealed record PartyResponse(Guid Id, string Name, double? Rating = null, int RatingCount = 0);

/// <summary>The code from the other person's screen, typed in at the handover.</summary>
public sealed record CodeRequest(string? Code);

/// <summary>What the damage cost, in TL, and what happened. At most the deposit.</summary>
public sealed record ClaimRequest(decimal Amount, string? Note);

public sealed record AnswerRequest(string? Note);

/// <summary>An administrator's decision: how much of the claim, in TL, is taken from the deposit.</summary>
public sealed record ResolveRequest(decimal Amount, string? Note);

public sealed record ReviewRequest(int Stars, string? Comment);

public sealed record BookingListingResponse(Guid Id, string Title, string? ThumbUrl, string Area);

/// <summary>What the rent comes to for a set of days, and whether they are free.</summary>
public sealed record QuoteResponse(
    DateOnly Start, DateOnly End, int Days, decimal PricePerDay, decimal FullPrice, decimal Discount, decimal Rent, decimal Deposit, bool Available, string? Reason, bool InstantBook);

public sealed record CalendarRange(Guid? Id, DateOnly Start, DateOnly End, string Kind, string? Note);

public sealed record BookingEventResponse(long Id, BookingEventKind Kind, string? ActorName, DateTimeOffset At, string Detail);

/// <summary>Where the money stands: the rent charge, any refund, and the holds on the renter's card.</summary>
public sealed record PaymentResponse(bool RentCharged, decimal Refund, bool Refunded, bool HoldsReleased, decimal DepositCharged, bool DepositSettled);

/// <summary>
/// The handover. The renter sees the pickup code (to show the owner), the owner sees the return code (to show the
/// renter); each is typed in by the other side, so neither can say a handover happened alone.
/// </summary>
public sealed record HandoverResponse(
    string? PickupCode, string? ReturnCode, DateTimeOffset? PickedUpAt, DateTimeOffset? ReturnedAt, DateTimeOffset? ClaimBy,
    bool CanPickUp, bool CanGiveBack, bool CanReportDamage, bool CanSignOff);

public sealed record ClaimResponse(
    decimal Amount, string Note, DateTimeOffset ClaimedAt, DateTimeOffset AnswerBy, ClaimAnswer? Answer, string AnswerNote, decimal? Taken, bool CanAnswer);

public sealed record HandoverPhotoResponse(Guid Id, HandoverStage Stage, string By, string Url, string ThumbUrl, int Width, int Height, DateTimeOffset At);

public sealed record ReviewResponse(Guid Id, string AuthorName, int Stars, string Comment, DateTimeOffset At);

public sealed record BookingReviews(bool CanReview, ReviewResponse? Mine, ReviewResponse? Theirs);

/// <summary>A claim waiting for an administrator, or for the renter.</summary>
public sealed record DisputeSummary(
    Guid BookingId, string ListingTitle, PartyResponse Owner, PartyResponse Renter, decimal Deposit, decimal Claim, string Note, DateTimeOffset ClaimedAt,
    ClaimAnswer? Answer, string AnswerNote, int Photos);

/// <summary>
/// One rental, as its owner or renter sees it. The fee and the owner's share are for the owner's eyes. The place is
/// exact for the owner, and for the renter once the rental is confirmed.
/// </summary>
public sealed record BookingResponse(
    Guid Id,
    BookingListingResponse Listing,
    PartyResponse Owner,
    PartyResponse Renter,
    DateOnly Start,
    DateOnly End,
    int Days,
    decimal Rent,
    decimal Deposit,
    decimal? Fee,
    decimal? OwnerShare,
    BookingStatus Status,
    string Message,
    string Reason,
    PlaceResponse Place,
    PaymentResponse Payment,
    DateTimeOffset RequestedAt,
    DateTimeOffset RespondBy,
    DateTimeOffset? ConfirmedAt,
    bool IsOwner,
    bool CanAccept,
    bool CanCancel,
    IReadOnlyList<BookingEventResponse> History,
    HandoverResponse Handover,
    ClaimResponse? Claim,
    IReadOnlyList<HandoverPhotoResponse> Photos,
    BookingReviews Reviews);

public sealed record BookingSummary(
    Guid Id, BookingListingResponse Listing, PartyResponse Other, DateOnly Start, DateOnly End, int Days, decimal Rent, BookingStatus Status, bool IsOwner,
    DateTimeOffset RequestedAt, DateTimeOffset RespondBy);

public sealed record LedgerLineResponse(Guid EntryId, string Kind, Guid? BookingId, string Memo, decimal Amount, DateTimeOffset PostedAt);

/// <summary>What the platform owes you, and how it got there.</summary>
public sealed record EarningsResponse(decimal Owed, decimal Earned, decimal Refunded, IReadOnlyList<LedgerLineResponse> Lines);

public sealed record AccountBalance(string Account, decimal Balance);

/// <summary>
/// A check of the books: every entry balances, the whole ledger sums to zero, and what the payment provider says it
/// holds equals what the ledger says is held there.
/// </summary>
public sealed record BooksResponse(
    bool Balanced, int Entries, int UnbalancedEntries, decimal LedgerTotal, decimal ProviderClearing, decimal ProviderSays, bool Reconciled,
    IReadOnlyList<AccountBalance> Accounts);
