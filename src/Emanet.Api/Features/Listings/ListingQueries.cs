using System.Globalization;
using System.Text.RegularExpressions;
using Emanet.Api.Common;
using Emanet.Core;
using Emanet.Core.Geo;
using Emanet.Core.Listings;
using Emanet.Infrastructure.Persistence;
using Emanet.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NpgsqlTypes;

namespace Emanet.Api.Features.Listings;

public sealed record SearchFilters(
    string? Q, Category? Category, double? Lat, double? Lng, double? RadiusKm, decimal? MinPrice, decimal? MaxPrice, DateOnly? From, DateOnly? To,
    ListingSort? Sort, int? Page, int? PageSize);

internal static partial class ListingQueries
{
    public const double DefaultRadiusKm = 25;
    public const double MaxRadiusKm = 200;

    public static string PhotoUrl(Guid photoId) => $"/api/photos/{photoId}";

    public static string ThumbUrl(Guid photoId) => $"/api/photos/{photoId}/thumb";

    /// <summary>
    /// The public search: words (stemmed as Turkish, with a plain match on the title for partial words), category,
    /// distance from a point and price. Only active listings, and only their approximate places, are ever used.
    /// </summary>
    public static async Task<Page<ListingSummary>> SearchAsync(EmanetDbContext db, SearchFilters f, CancellationToken ct)
    {
        var (page, size) = Paging.Clamp(f.Page, f.PageSize);
        var query = db.Listings.AsNoTracking().Where(l => l.Status == ListingStatus.Active);
        if (f.Category is { } category)
        {
            query = query.Where(l => l.Category == category);
        }

        var words = string.IsNullOrWhiteSpace(f.Q) ? null : f.Q.Trim();
        if (words is { Length: > 100 })
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Search for at most 100 characters.");
        }

        var prefixes = words is null ? null : Prefixes(words);
        if (words is not null && prefixes is not null)
        {
            query = query.Where(l => EF.Property<NpgsqlTsVector>(l, ListingConfiguration.SearchColumn)
                .Matches(EF.Functions.WebSearchToTsQuery("turkish", words).Or(EF.Functions.ToTsQuery("simple", prefixes))));
        }

        Point? origin = null;
        if (f.Lat is { } lat && f.Lng is { } lng)
        {
            origin = GeoPoint.At(lat, lng);
            var radius = f.RadiusKm ?? DefaultRadiusKm;
            if (radius is <= 0 or > MaxRadiusKm)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, $"Search within 0 to {MaxRadiusKm} km.");
            }

            query = query.Where(l => l.PublicLocation.IsWithinDistance(origin, radius * 1000));
        }
        else if (f.Lat is not null || f.Lng is not null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Send both lat and lng, or neither.");
        }

        if (f.MinPrice is { } min)
        {
            var minor = Money.ToMinor(min, "The lowest price");
            query = query.Where(l => l.PricePerDayMinor >= minor);
        }

        if (f.MaxPrice is { } max)
        {
            var minor = Money.ToMinor(max, "The highest price");
            query = query.Where(l => l.PricePerDayMinor <= minor);
        }

        if (f.From is { } from && f.To is { } to)
        {
            if (to <= from)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, "The return day comes after the pickup day.");
            }

            var days = to.DayNumber - from.DayNumber;
            query = query.Where(l => l.MinDays <= days && days <= l.MaxDaysPerRental && !db.Calendar.Any(c => c.ListingId == l.Id && c.Start < to && from < c.End));
        }
        else if (f.From is not null || f.To is not null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Send both from and to, or neither.");
        }

        var sort = f.Sort ?? (words is not null ? ListingSort.Relevance : origin is not null ? ListingSort.Distance : ListingSort.Newest);
        if (sort == ListingSort.Distance && origin is null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Sorting by distance needs lat and lng.");
        }

        query = sort switch
        {
            ListingSort.Distance => query.OrderBy(l => l.PublicLocation.Distance(origin!)).ThenBy(l => l.Id),
            ListingSort.PriceLow => query.OrderBy(l => l.PricePerDayMinor).ThenBy(l => l.Id),
            ListingSort.PriceHigh => query.OrderByDescending(l => l.PricePerDayMinor).ThenBy(l => l.Id),
            ListingSort.Relevance when words is not null && prefixes is not null => query
                .OrderByDescending(l => EF.Property<NpgsqlTsVector>(l, ListingConfiguration.SearchColumn)
                    .Rank(EF.Functions.WebSearchToTsQuery("turkish", words).Or(EF.Functions.ToTsQuery("simple", prefixes))))
                .ThenByDescending(l => l.CreatedAt),
            _ => query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id),
        };

        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * size).Take(size)
            .Select(l => new
            {
                Listing = l,
                Owner = db.Users.Where(u => u.Id == l.OwnerId).Select(u => u.DisplayName).First(),
                Thumb = db.ListingPhotos.Where(p => p.ListingId == l.Id).OrderBy(p => p.Position).Select(p => (Guid?)p.Id).FirstOrDefault(),
                Photos = db.ListingPhotos.Count(p => p.ListingId == l.Id),
                Metres = origin == null ? (double?)null : l.PublicLocation.Distance(origin),
            })
            .ToListAsync(ct);
        var items = rows.Select(r => Summary(r.Listing, r.Owner, r.Thumb, r.Photos, r.Metres)).ToList();
        return new Page<ListingSummary>(items, total, page, size);
    }

    /// <summary>The owner's own listings, paused ones included, newest first.</summary>
    public static async Task<List<ListingSummary>> MineAsync(EmanetDbContext db, Guid ownerId, CancellationToken ct)
    {
        var rows = await db.Listings.AsNoTracking()
            .Where(l => l.OwnerId == ownerId && l.Status != ListingStatus.Removed)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new
            {
                Listing = l,
                Owner = db.Users.Where(u => u.Id == l.OwnerId).Select(u => u.DisplayName).First(),
                Thumb = db.ListingPhotos.Where(p => p.ListingId == l.Id).OrderBy(p => p.Position).Select(p => (Guid?)p.Id).FirstOrDefault(),
                Photos = db.ListingPhotos.Count(p => p.ListingId == l.Id),
            })
            .ToListAsync(ct);
        return rows.Select(r => Summary(r.Listing, r.Owner, r.Thumb, r.Photos, null)).ToList();
    }

    public static async Task<ListingResponse> DetailAsync(EmanetDbContext db, Guid listingId, Guid? viewerId, CancellationToken ct)
    {
        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId, ct);
        var mine = listing is not null && viewerId == listing.OwnerId;
        var renting = listing is not null && viewerId is { } viewer && !mine && await db.Bookings.AnyAsync(
            b => b.ListingId == listing.Id && b.RenterId == viewer && (b.Status == Core.Bookings.BookingStatus.Confirmed || b.Status == Core.Bookings.BookingStatus.Active), ct);
        // A removed listing is gone for everyone; a paused one stays visible to its owner and to links people already have.
        if (listing is null || listing.Status == ListingStatus.Removed)
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no listing with this id.");
        }

        var owner = await db.Users.AsNoTracking().Where(u => u.Id == listing.OwnerId)
            .Select(u => new OwnerResponse(u.Id, u.DisplayName, u.CreatedAt, db.Listings.Count(l => l.OwnerId == u.Id && l.Status == ListingStatus.Active)))
            .SingleAsync(ct);
        var photos = await db.ListingPhotos.AsNoTracking().Where(p => p.ListingId == listingId).OrderBy(p => p.Position)
            .Select(p => new { p.Id, p.Width, p.Height })
            .ToListAsync(ct);
        var place = PlaceFor(listing, exact: mine || renting);
        return new ListingResponse(
            listing.Id, listing.Title, listing.Description, listing.Category,
            Money.FromMinor(listing.PricePerDayMinor), listing.WeeklyPercentOff, Money.FromMinor(listing.DepositMinor),
            listing.MinDays, listing.MaxDaysPerRental, listing.InstantBook, place,
            photos.Select(p => new PhotoResponse(p.Id, PhotoUrl(p.Id), ThumbUrl(p.Id), p.Width, p.Height)).ToList(),
            owner, listing.Rating, listing.RatingCount, listing.Status, mine, listing.CreatedAt, listing.UpdatedAt);
    }

    /// <summary>
    /// "kamp sandaly" as "kamp:* &amp; sandaly:*": every word, as the start of a word. Only letters and digits get
    /// through, so nothing the user types can change the query's shape. Null when no word is left.
    /// </summary>
    internal static string? Prefixes(string words)
    {
        var tokens = Words().Matches(words.ToLower(CultureInfo.GetCultureInfo("tr-TR"))).Select(m => m.Value).Take(8).ToList();
        return tokens.Count == 0 ? null : string.Join(" & ", tokens.Select(t => $"{t}:*"));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();

    public static PlaceResponse PlaceFor(Listing listing, bool exact) =>
        exact
            ? new PlaceResponse(listing.Location.Latitude(), listing.Location.Longitude(), listing.Area, true)
            : new PlaceResponse(listing.PublicLocation.Latitude(), listing.PublicLocation.Longitude(), listing.Area, false);

    private static ListingSummary Summary(Listing l, string owner, Guid? thumb, int photos, double? metres) => new(
        l.Id, l.Title, l.Category, Money.FromMinor(l.PricePerDayMinor), l.WeeklyPercentOff, Money.FromMinor(l.DepositMinor), l.Area,
        l.PublicLocation.Latitude(), l.PublicLocation.Longitude(), metres is { } m ? Math.Round(m / 1000, 1) : null,
        thumb is { } id ? ThumbUrl(id) : null, photos, l.Rating, l.RatingCount, owner, l.Status);
}
