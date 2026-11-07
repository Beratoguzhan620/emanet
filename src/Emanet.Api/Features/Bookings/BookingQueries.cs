using Emanet.Api.Common;
using Emanet.Api.Features.Listings;
using Emanet.Core;
using Emanet.Core.Bookings;
using Emanet.Core.Reviews;
using Emanet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Features.Bookings;

internal static class BookingQueries
{
    /// <summary>A rental its owner or renter may see; to anyone else it does not exist.</summary>
    public static async Task<Booking> FindAsync(EmanetDbContext db, Guid bookingId, Guid userId, CancellationToken ct, bool admin = false)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        return booking is not null && (admin || booking.InvolvesUser(userId)) ? booking : throw new DomainException(ErrorCodes.NotFound, "There is no rental with this id.");
    }

    public static string PhotoUrl(Guid bookingId, Guid photoId) => $"/api/bookings/{bookingId}/photos/{photoId}";

    /// <summary>One rental for its owner, its renter, or an administrator looking at a claim (who sees it as neither side).</summary>
    public static async Task<BookingResponse> DetailAsync(EmanetDbContext db, Guid bookingId, Guid viewerId, DateTimeOffset now, CancellationToken ct, bool admin = false)
    {
        var booking = await FindAsync(db, bookingId, viewerId, ct, admin);
        var listing = await db.Listings.AsNoTracking().SingleAsync(l => l.Id == booking.ListingId, ct);
        var thumb = await db.ListingPhotos.AsNoTracking().Where(p => p.ListingId == listing.Id).OrderBy(p => p.Position).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        var people = await db.Users.AsNoTracking().Where(u => u.Id == booking.OwnerId || u.Id == booking.RenterId)
            .Select(u => new { u.Id, u.DisplayName, u.RatingSum, u.RatingCount }).ToDictionaryAsync(u => u.Id, ct);
        var history = await (
            from e in db.BookingEvents.AsNoTracking()
            where e.BookingId == bookingId
            orderby e.Id
            select new BookingEventResponse(e.Id, e.Kind, e.ActorId == null ? null : db.Users.Where(u => u.Id == e.ActorId).Select(u => u.DisplayName).First(), e.At, e.Detail))
            .ToListAsync(ct);
        var photos = await db.HandoverPhotos.AsNoTracking().Where(p => p.BookingId == bookingId).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.BookingId == bookingId).ToListAsync(ct);

        var isOwner = booking.OwnerId == viewerId;
        var isRenter = booking.RenterId == viewerId;
        var today = Clock.Today(now);
        PartyResponse Party(Guid id)
        {
            var person = people[id];
            return new PartyResponse(id, person.DisplayName, person.RatingCount == 0 ? null : Math.Round((double)person.RatingSum / person.RatingCount, 1), person.RatingCount);
        }

        string Name(Guid id) => people.TryGetValue(id, out var p) ? p.DisplayName : "Support";
        ReviewResponse? AsResponse(Review? r) => r is null ? null : new ReviewResponse(r.Id, Name(r.AuthorId), r.Stars, r.Comment, r.CreatedAt);

        var handover = new HandoverResponse(
            isRenter && booking.Status == BookingStatus.Confirmed ? booking.PickupCode : null,
            isOwner && booking.Status == BookingStatus.Active ? booking.ReturnCode : null,
            booking.PickedUpAt, booking.ReturnedAt, booking.ClaimBy,
            isOwner && booking.Status == BookingStatus.Confirmed && booking.Start <= today && today < booking.End,
            isRenter && booking.Status == BookingStatus.Active,
            isOwner && booking.Status == BookingStatus.Returned && now < booking.ClaimBy,
            isOwner && booking.Status == BookingStatus.Returned);
        var claim = booking.ClaimedAt is { } claimedAt
            ? new ClaimResponse(
                Money.FromMinor(booking.ClaimMinor), booking.ClaimNote, claimedAt, booking.AnswerBy!.Value, booking.RenterAnswer, booking.RenterAnswerNote,
                booking.Status == BookingStatus.Completed ? Money.FromMinor(booking.DamageMinor) : null,
                isRenter && booking.Status == BookingStatus.Disputed && booking.RenterAnswer is null && now < booking.AnswerBy)
            : null;
        var mine = reviews.SingleOrDefault(r => r.AuthorId == viewerId);
        var theirs = reviews.SingleOrDefault(r => r.AuthorId != viewerId && (isOwner || isRenter));

        return new BookingResponse(
            booking.Id,
            new BookingListingResponse(listing.Id, listing.Title, thumb is { } t ? ListingQueries.ThumbUrl(t) : null, listing.Area),
            Party(booking.OwnerId),
            Party(booking.RenterId),
            booking.Start, booking.End, booking.Days,
            Money.FromMinor(booking.RentMinor), Money.FromMinor(booking.DepositMinor),
            isOwner || admin ? Money.FromMinor(booking.FeeMinor) : null,
            isOwner || admin ? Money.FromMinor(booking.OwnerShareMinor) : null,
            booking.Status, booking.Message, booking.Reason,
            ListingQueries.PlaceFor(listing, exact: isOwner || (isRenter && booking.RevealsPlace)),
            new PaymentResponse(booking.RentChargedAt is not null, Money.FromMinor(booking.RefundMinor), booking.RefundedAt is not null, booking.HoldsReleasedAt is not null,
                Money.FromMinor(booking.DepositSettledAt is null ? 0 : booking.DamageMinor), booking.DepositSettledAt is not null),
            booking.RequestedAt, booking.RespondBy, booking.ConfirmedAt,
            isOwner,
            isOwner && booking.Status == BookingStatus.Requested && now < booking.RespondBy,
            (isOwner || isRenter) && booking.Status is BookingStatus.Requested or BookingStatus.Confirmed,
            history,
            handover,
            claim,
            photos.Select(p => new HandoverPhotoResponse(p.Id, p.Stage, Name(p.UploaderId), PhotoUrl(booking.Id, p.Id), PhotoUrl(booking.Id, p.Id) + "/thumb", p.Width, p.Height, p.CreatedAt)).ToList(),
            new BookingReviews((isOwner || isRenter) && mine is null && booking.CanBeReviewed(now), AsResponse(mine), AsResponse(theirs)));
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
