using Emanet.Core.Listings;

namespace Emanet.Api.Features.Listings;

/// <summary>A listing as its owner writes it. Amounts are in lira.</summary>
public sealed record ListingRequest(
    string Title,
    string? Description,
    Category Category,
    decimal PricePerDay,
    int WeeklyPercentOff,
    decimal Deposit,
    double Latitude,
    double Longitude,
    string Area,
    int MinDays,
    int MaxDays,
    bool InstantBook = false);

/// <summary>
/// Where an item is. <c>exact</c> is true only for its owner (and, later, a renter with a confirmed rental);
/// everyone else gets the centre of a cell a few hundred metres wide.
/// </summary>
public sealed record PlaceResponse(double Latitude, double Longitude, string Area, bool Exact);

public sealed record PhotoResponse(Guid Id, string Url, string ThumbUrl, int Width, int Height);

public sealed record OwnerResponse(Guid Id, string Name, DateTimeOffset MemberSince, int ActiveListings);

/// <summary>One result in a search: enough for a card on a list or a pin on a map.</summary>
public sealed record ListingSummary(
    Guid Id,
    string Title,
    Category Category,
    decimal PricePerDay,
    int WeeklyPercentOff,
    decimal Deposit,
    string Area,
    double Latitude,
    double Longitude,
    double? DistanceKm,
    string? ThumbUrl,
    int Photos,
    double? Rating,
    int RatingCount,
    string OwnerName,
    ListingStatus Status);

public sealed record ListingResponse(
    Guid Id,
    string Title,
    string Description,
    Category Category,
    decimal PricePerDay,
    int WeeklyPercentOff,
    decimal Deposit,
    int MinDays,
    int MaxDays,
    bool InstantBook,
    PlaceResponse Place,
    IReadOnlyList<PhotoResponse> Photos,
    OwnerResponse Owner,
    double? Rating,
    int RatingCount,
    ListingStatus Status,
    bool IsMine,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum ListingSort
{
    /// <summary>Best text match first when there is a query; otherwise the newest.</summary>
    Relevance = 0,
    Distance = 1,
    PriceLow = 2,
    PriceHigh = 3,
    Newest = 4,
}

public sealed record CategoryResponse(Category Key, int ActiveListings);
