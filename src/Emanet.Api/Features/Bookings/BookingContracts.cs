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

public sealed record PartyResponse(Guid Id, string Name);

public sealed record BookingListingResponse(Guid Id, string Title, string? ThumbUrl, string Area);

/// <summary>What the rent comes to for a set of days, and whether they are free.</summary>
public sealed record QuoteResponse(
    DateOnly Start, DateOnly End, int Days, decimal PricePerDay, decimal FullPrice, decimal Discount, decimal Rent, decimal Deposit, bool Available, string? Reason, bool InstantBook);

public sealed record CalendarRange(Guid? Id, DateOnly Start, DateOnly End, string Kind, string? Note);

public sealed record BookingEventResponse(long Id, BookingEventKind Kind, string? ActorName, DateTimeOffset At, string Detail);

/// <summary>Where the money stands: the rent charge, any refund, and the holds on the renter's card.</summary>
public sealed record PaymentResponse(bool RentCharged, decimal Refund, bool Refunded, bool HoldsReleased);

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
    IReadOnlyList<BookingEventResponse> History);

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
