namespace Emanet.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record MeDto(UserDto User, DateTimeOffset MemberSince, int ActiveListings, bool IsAdmin);

public sealed record PageDto<T>(List<T> Items, int Total, int PageNumber, int PageSize);

public sealed record PlaceDto(double Latitude, double Longitude, string Area, bool Exact);

public sealed record PhotoDto(Guid Id, string Url, string ThumbUrl, int Width, int Height);

public sealed record OwnerDto(Guid Id, string Name, DateTimeOffset MemberSince, int ActiveListings);

public sealed record ListingSummaryDto(
    Guid Id, string Title, string Category, decimal PricePerDay, int WeeklyPercentOff, decimal Deposit, string Area, double Latitude, double Longitude,
    double? DistanceKm, string? ThumbUrl, int Photos, double? Rating, int RatingCount, string OwnerName, string Status);

public sealed record ListingDto(
    Guid Id, string Title, string Description, string Category, decimal PricePerDay, int WeeklyPercentOff, decimal Deposit, int MinDays, int MaxDays,
    bool InstantBook, PlaceDto Place, List<PhotoDto> Photos, OwnerDto Owner, double? Rating, int RatingCount, string Status, bool IsMine, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CategoryDto(string Key, int ActiveListings);

public sealed record PartyDto(Guid Id, string Name, double? Rating, int RatingCount);

public sealed record BookingListingDto(Guid Id, string Title, string? ThumbUrl, string Area);

public sealed record PaymentDto(bool RentCharged, decimal Refund, bool Refunded, bool HoldsReleased, decimal DepositCharged, bool DepositSettled);

public sealed record HandoverDto(
    string? PickupCode, string? ReturnCode, DateTimeOffset? PickedUpAt, DateTimeOffset? ReturnedAt, DateTimeOffset? ClaimBy,
    bool CanPickUp, bool CanGiveBack, bool CanReportDamage, bool CanSignOff);

public sealed record ClaimDto(decimal Amount, string Note, DateTimeOffset ClaimedAt, DateTimeOffset AnswerBy, string? Answer, string AnswerNote, decimal? Taken, bool CanAnswer);

public sealed record HandoverPhotoDto(Guid Id, string Stage, string By, string Url, string ThumbUrl, int Width, int Height, DateTimeOffset At);

public sealed record ReviewDto(Guid Id, string AuthorName, int Stars, string Comment, DateTimeOffset At);

public sealed record BookingReviewsDto(bool CanReview, ReviewDto? Mine, ReviewDto? Theirs);

public sealed record DisputeDto(
    Guid BookingId, string ListingTitle, PartyDto Owner, PartyDto Renter, decimal Deposit, decimal Claim, string Note, DateTimeOffset ClaimedAt,
    string? Answer, string AnswerNote, int Photos);

public sealed record BookingEventDto(long Id, string Kind, string? ActorName, DateTimeOffset At, string Detail);

public sealed record BookingDto(
    Guid Id, BookingListingDto Listing, PartyDto Owner, PartyDto Renter, DateOnly Start, DateOnly End, int Days, decimal Rent, decimal Deposit, decimal? Fee,
    decimal? OwnerShare, string Status, string Message, string Reason, PlaceDto Place, PaymentDto Payment, DateTimeOffset RequestedAt, DateTimeOffset RespondBy,
    DateTimeOffset? ConfirmedAt, bool IsOwner, bool CanAccept, bool CanCancel, List<BookingEventDto> History, HandoverDto Handover, ClaimDto? Claim,
    List<HandoverPhotoDto> Photos, BookingReviewsDto Reviews);

public sealed record BookingSummaryDto(
    Guid Id, BookingListingDto Listing, PartyDto Other, DateOnly Start, DateOnly End, int Days, decimal Rent, string Status, bool IsOwner,
    DateTimeOffset RequestedAt, DateTimeOffset RespondBy);

public sealed record QuoteDto(
    DateOnly Start, DateOnly End, int Days, decimal PricePerDay, decimal FullPrice, decimal Discount, decimal Rent, decimal Deposit, bool Available, string? Reason, bool InstantBook);

public sealed record CalendarRangeDto(Guid? Id, DateOnly Start, DateOnly End, string Kind, string? Note);

public sealed record LedgerLineDto(Guid EntryId, string Kind, Guid? BookingId, string Memo, decimal Amount, DateTimeOffset PostedAt);

public sealed record EarningsDto(decimal Owed, decimal Earned, decimal Refunded, List<LedgerLineDto> Lines);

public sealed record AccountBalanceDto(string Account, decimal Balance);

public sealed record BooksDto(
    bool Balanced, int Entries, int UnbalancedEntries, decimal LedgerTotal, decimal ProviderClearing, decimal ProviderSays, bool Reconciled, List<AccountBalanceDto> Accounts);
