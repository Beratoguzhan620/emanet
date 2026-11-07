using Emanet.Api.Common;
using Emanet.Api.Features.Listings;
using Emanet.Core;
using Emanet.Core.Bookings;
using Emanet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Features.Bookings;

internal static class BookingQueries
{
    /// <summary>A rental its owner or renter may see; to anyone else it does not exist.</summary>
    public static async Task<Booking> FindAsync(EmanetDbContext db, Guid bookingId, Guid userId, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        return booking is not null && booking.InvolvesUser(userId) ? booking : throw new DomainException(ErrorCodes.NotFound, "There is no rental with this id.");
    }

    public static async Task<BookingResponse> DetailAsync(EmanetDbContext db, Guid bookingId, Guid viewerId, DateTimeOffset now, CancellationToken ct)
    {
        var booking = await FindAsync(db, bookingId, viewerId, ct);
        var listing = await db.Listings.AsNoTracking().SingleAsync(l => l.Id == booking.ListingId, ct);
        var thumb = await db.ListingPhotos.AsNoTracking().Where(p => p.ListingId == listing.Id).OrderBy(p => p.Position).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        var names = await db.Users.AsNoTracking().Where(u => u.Id == booking.OwnerId || u.Id == booking.RenterId).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var history = await (
            from e in db.BookingEvents.AsNoTracking()
            where e.BookingId == bookingId
            orderby e.Id
            select new BookingEventResponse(e.Id, e.Kind, e.ActorId == null ? null : db.Users.Where(u => u.Id == e.ActorId).Select(u => u.DisplayName).First(), e.At, e.Detail))
            .ToListAsync(ct);
        var isOwner = booking.OwnerId == viewerId;
        return new BookingResponse(
            booking.Id,
            new BookingListingResponse(listing.Id, listing.Title, thumb is { } t ? ListingQueries.ThumbUrl(t) : null, listing.Area),
            new PartyResponse(booking.OwnerId, names[booking.OwnerId]),
            new PartyResponse(booking.RenterId, names[booking.RenterId]),
            booking.Start, booking.End, booking.Days,
            Money.FromMinor(booking.RentMinor), Money.FromMinor(booking.DepositMinor),
            isOwner ? Money.FromMinor(booking.FeeMinor) : null,
            isOwner ? Money.FromMinor(booking.OwnerShareMinor) : null,
            booking.Status, booking.Message, booking.Reason,
            ListingQueries.PlaceFor(listing, exact: isOwner || booking.RevealsPlace),
            new PaymentResponse(booking.RentChargedAt is not null, Money.FromMinor(booking.RefundMinor), booking.RefundedAt is not null, booking.HoldsReleasedAt is not null),
            booking.RequestedAt, booking.RespondBy, booking.ConfirmedAt,
            isOwner,
            isOwner && booking.Status == BookingStatus.Requested && now < booking.RespondBy,
            booking.Status is BookingStatus.Requested or BookingStatus.Confirmed,
            history);
    }

    public static async Task<Page<BookingSummary>> MineAsync(EmanetDbContext db, Guid userId, string? role, BookingStatus[]? statuses, int? page, int? pageSize, CancellationToken ct)
    {
        var (number, size) = Paging.Clamp(page, pageSize);
        var query = db.Bookings.AsNoTracking().Where(b => role == "owner" ? b.OwnerId == userId : role == "renter" ? b.RenterId == userId : b.OwnerId == userId || b.RenterId == userId);
        if (statuses is { Length: > 0 })
        {
            query = query.Where(b => statuses.Contains(b.Status));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(b => b.RequestedAt).Skip((number - 1) * size).Take(size)
            .Select(b => new
            {
                Booking = b,
                Listing = db.Listings.Where(l => l.Id == b.ListingId).Select(l => new { l.Id, l.Title, l.Area }).First(),
                Thumb = db.ListingPhotos.Where(p => p.ListingId == b.ListingId).OrderBy(p => p.Position).Select(p => (Guid?)p.Id).FirstOrDefault(),
                OtherName = db.Users.Where(u => u.Id == (b.OwnerId == userId ? b.RenterId : b.OwnerId)).Select(u => u.DisplayName).First(),
            })
            .ToListAsync(ct);
        var items = rows.Select(r => new BookingSummary(
            r.Booking.Id,
            new BookingListingResponse(r.Listing.Id, r.Listing.Title, r.Thumb is { } t ? ListingQueries.ThumbUrl(t) : null, r.Listing.Area),
            new PartyResponse(r.Booking.OwnerId == userId ? r.Booking.RenterId : r.Booking.OwnerId, r.OtherName),
            r.Booking.Start, r.Booking.End, r.Booking.Days, Money.FromMinor(r.Booking.RentMinor), r.Booking.Status, r.Booking.OwnerId == userId,
            r.Booking.RequestedAt, r.Booking.RespondBy)).ToList();
        return new Page<BookingSummary>(items, total, number, size);
    }
}
