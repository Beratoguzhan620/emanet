namespace Emanet.IntegrationTests.Support;

public sealed record UserDto(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record MeDto(UserDto User, DateTimeOffset MemberSince, int ActiveListings);

public sealed record PageDto<T>(List<T> Items, int Total, int PageNumber, int PageSize);

public sealed record PlaceDto(double Latitude, double Longitude, string Area, bool Exact);

public sealed record PhotoDto(Guid Id, string Url, string ThumbUrl, int Width, int Height);

public sealed record OwnerDto(Guid Id, string Name, DateTimeOffset MemberSince, int ActiveListings);

public sealed record ListingSummaryDto(
    Guid Id, string Title, string Category, decimal PricePerDay, int WeeklyPercentOff, decimal Deposit, string Area, double Latitude, double Longitude,
    double? DistanceKm, string? ThumbUrl, int Photos, double? Rating, int RatingCount, string OwnerName, string Status);

public sealed record ListingDto(
    Guid Id, string Title, string Description, string Category, decimal PricePerDay, int WeeklyPercentOff, decimal Deposit, int MinDays, int MaxDays,
    PlaceDto Place, List<PhotoDto> Photos, OwnerDto Owner, double? Rating, int RatingCount, string Status, bool IsMine, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CategoryDto(string Key, int ActiveListings);
