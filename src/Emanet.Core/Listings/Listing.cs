using Emanet.Core.Geo;
using NetTopologySuite.Geometries;

namespace Emanet.Core.Listings;

public enum ListingStatus
{
    /// <summary>Shown in search and open to requests.</summary>
    Active = 0,

    /// <summary>Hidden from search by its owner; bookings already made still go ahead.</summary>
    Paused = 1,

    /// <summary>Taken down for good. Kept so past rentals keep their history.</summary>
    Removed = 2,
}

/// <summary>Something a person rents out: what it is, what it costs a day, the deposit, and where to collect it.</summary>
public sealed class Listing
{
    public const int TitleMin = 5;
    public const int TitleMax = 80;
    public const int DescriptionMax = 2000;
    public const int AreaMax = 80;

    /// <summary>One lira a day at least, one hundred thousand at most, in kuruş.</summary>
    public const long MinPricePerDay = 100;
    public const long MaxPricePerDay = 100_000_00;
    public const long MaxDeposit = 1_000_000_00;
    public const int MaxWeeklyPercentOff = 50;
    public const int MaxDays = 90;

    private Listing()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Category Category { get; private set; }

    public long PricePerDayMinor { get; private set; }

    /// <summary>Taken off the whole rent when it runs seven days or more.</summary>
    public int WeeklyPercentOff { get; private set; }

    public long DepositMinor { get; private set; }

    /// <summary>The exact pickup point. Shown to the owner and, once a rental is confirmed, to the renter.</summary>
    public Point Location { get; private set; } = null!;

    /// <summary>
    /// The centre of the grid cell the pickup point is in. Search filters, sorts and measures distance with this, so
    /// no number of searches from different places can narrow down the real address.
    /// </summary>
    public Point PublicLocation { get; private set; } = null!;

    /// <summary>A neighbourhood and city, such as "Atakum, Samsun": what everyone else sees.</summary>
    public string Area { get; private set; } = string.Empty;

    public int MinDays { get; private set; }

    public int MaxDaysPerRental { get; private set; }

    /// <summary>Requests for free dates are confirmed at once, without waiting for the owner.</summary>
    public bool InstantBook { get; private set; }

    public ListingStatus Status { get; private set; }

    public int RatingSum { get; private set; }

    public int RatingCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Listing Create(
        Guid ownerId, string title, string? description, Category category, long pricePerDayMinor, int weeklyPercentOff, long depositMinor,
        double latitude, double longitude, string area, int minDays, int maxDays, DateTimeOffset now, bool instantBook = false)
    {
        var listing = new Listing { Id = Guid.CreateVersion7(now), OwnerId = ownerId, Status = ListingStatus.Active, CreatedAt = now };
        listing.Change(title, description, category, pricePerDayMinor, weeklyPercentOff, depositMinor, latitude, longitude, area, minDays, maxDays, now, instantBook);
        return listing;
    }

    public void Change(
        string title, string? description, Category category, long pricePerDayMinor, int weeklyPercentOff, long depositMinor,
        double latitude, double longitude, string area, int minDays, int maxDays, DateTimeOffset now, bool instantBook = false)
    {
        EnsureNotRemoved();
        if (!Enum.IsDefined(category))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Pick one of the categories.");
        }

        if (pricePerDayMinor is < MinPricePerDay or > MaxPricePerDay)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A day costs from 1 to 100,000 TL.");
        }

        if (weeklyPercentOff is < 0 or > MaxWeeklyPercentOff)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"The weekly discount is 0 to {MaxWeeklyPercentOff} percent.");
        }

        if (depositMinor is < 0 or > MaxDeposit)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The deposit is 0 to 1,000,000 TL.");
        }

        if (minDays < 1 || maxDays < minDays || maxDays > MaxDays)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A rental lasts at least 1 day, at most {MaxDays}, and the shortest cannot be longer than the longest.");
        }

        Title = Text.Clean(title, TitleMin, TitleMax, "A title");
        Description = Text.CleanParagraphs(description, DescriptionMax, "A description");
        Category = category;
        PricePerDayMinor = pricePerDayMinor;
        WeeklyPercentOff = weeklyPercentOff;
        DepositMinor = depositMinor;
        Location = GeoPoint.At(latitude, longitude);
        var (publicLatitude, publicLongitude) = GeoPoint.Approximate(Location);
        PublicLocation = GeoPoint.At(publicLatitude, publicLongitude);
        Area = Text.Clean(area, 2, AreaMax, "An area");
        MinDays = minDays;
        MaxDaysPerRental = maxDays;
        InstantBook = instantBook;
        UpdatedAt = now;
    }

    public bool IsOwnedBy(Guid userId) => OwnerId == userId;

    public void Pause(DateTimeOffset now)
    {
        EnsureNotRemoved();
        Status = ListingStatus.Paused;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        EnsureNotRemoved();
        Status = ListingStatus.Active;
        UpdatedAt = now;
    }

    public void Remove(DateTimeOffset now)
    {
        Status = ListingStatus.Removed;
        UpdatedAt = now;
    }

    /// <summary>The rent for a number of days, with the weekly discount from the seventh day on, in kuruş.</summary>
    public long RentFor(int days)
    {
        if (days < MinDays || days > MaxDaysPerRental)
        {
            throw new DomainException(ErrorCodes.ValidationFailed,
                MinDays == MaxDaysPerRental ? $"This item is rented for exactly {MinDays} days." : $"This item is rented for {MinDays} to {MaxDaysPerRental} days.");
        }

        var full = PricePerDayMinor * days;
        return days >= 7 && WeeklyPercentOff > 0 ? (long)Math.Round(full * (100 - WeeklyPercentOff) / 100m, MidpointRounding.AwayFromZero) : full;
    }

    public void Rate(int stars)
    {
        RatingSum += stars;
        RatingCount++;
    }

    public double? Rating => RatingCount == 0 ? null : Math.Round((double)RatingSum / RatingCount, 1);

    private void EnsureNotRemoved()
    {
        if (Status == ListingStatus.Removed)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This listing was removed.");
        }
    }
}

/// <summary>A photo of a listing, kept in two sizes with its metadata (location included) stripped.</summary>
public sealed class ListingPhoto
{
    public const int MaxPerListing = 8;

    private ListingPhoto()
    {
    }

    public Guid Id { get; private set; }

    public Guid ListingId { get; private set; }

    public int Position { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public long SizeBytes { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public string FullKey => $"listings/{ListingId}/{Id}.jpg";

    public string ThumbKey => $"listings/{ListingId}/{Id}-thumb.jpg";

    public static ListingPhoto Create(Guid listingId, int position, int width, int height, long sizeBytes, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), ListingId = listingId, Position = position, Width = width, Height = height, SizeBytes = sizeBytes, UploadedAt = now };

    public void MoveTo(int position) => Position = position;
}
