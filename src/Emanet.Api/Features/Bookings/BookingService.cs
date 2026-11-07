using Emanet.Core;
using Emanet.Core.Bookings;
using Emanet.Core.Ledger;
using Emanet.Core.Listings;
using Emanet.Core.Payments;
using Emanet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Features.Bookings;

/// <summary>
/// The rental's life, from request to settlement. Dates and statuses change inside database transactions; money
/// moves after them, through <see cref="SettleAsync"/>, which is keyed and safe to run again, so a provider that is
/// down for a minute delays a charge but never loses or doubles one. The sweeper settles whatever was left over.
/// </summary>
public sealed partial class BookingService(EmanetDbContext db, IPaymentProvider payments, TimeProvider clock, ILogger<BookingService> logger)
{
    public async Task<Booking> RequestAsync(Guid renterId, Guid listingId, DateOnly start, DateOnly end, string? message, string paymentMethod, CancellationToken ct)
    {
        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId && l.Status != ListingStatus.Removed, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no listing with this id.");
        var now = clock.GetUtcNow();
        var booking = Booking.Request(listing, renterId, start, end, message, paymentMethod, now);
        // A quick look first, so nobody's card is held for days that are plainly taken. The constraint has the last word.
        if (await TakenAsync(listingId, start, end, ct))
        {
            throw new DomainException(ErrorCodes.Unavailable, "Some of these days are taken. Pick others.");
        }

        string rentHold;
        string? depositHold = null;
        try
        {
            rentHold = (await payments.HoldAsync(booking.RentMinor, booking.PaymentMethod, $"booking:{booking.Id}:hold-rent", ct)).Id;
            if (booking.DepositMinor > 0)
            {
                try
                {
                    depositHold = (await payments.HoldAsync(booking.DepositMinor, booking.PaymentMethod, $"booking:{booking.Id}:hold-deposit", ct)).Id;
                }
                catch (PaymentDeclinedException)
                {
                    await payments.ReleaseAsync(rentHold, $"booking:{booking.Id}:release-rent", ct);
                    throw;
                }
            }
        }
        catch (PaymentDeclinedException ex)
        {
            throw new DomainException(ErrorCodes.PaymentDeclined, ex.Reason);
        }

        booking.Held(rentHold, depositHold);
        db.Bookings.Add(booking);
        db.BookingEvents.Add(BookingEvent.Record(booking, BookingEventKind.Requested, renterId, now, $"{booking.Days} {(booking.Days == 1 ? "day" : "days")}"));
        await db.SaveChangesAsync(ct);
        LogRequested(logger, booking.Id, listingId);

        if (listing.InstantBook)
        {
            await ConfirmAsync(booking.Id, actorId: null, ct);
        }

        return booking;
    }

    /// <summary>
    /// Takes the days for the rental. Inside one transaction: the rental becomes confirmed, its days go into the
    /// calendar, and other requests for any of those days are declined. If someone else got a day first, the
    /// exclusion constraint refuses the calendar row; this request is then declined and its holds let go.
    /// </summary>
    public async Task ConfirmAsync(Guid bookingId, Guid? actorId, CancellationToken ct)
    {
        var touched = new List<Guid> { bookingId };
        var now = clock.GetUtcNow();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Confirmations of one listing take turns. Each one declines the others' requests, so two running side
            // by side would wait on each other's rows; in turn, the later one finds its days taken and stops.
            var listingId = await db.Bookings.Where(b => b.Id == bookingId).Select(b => (Guid?)b.ListingId).SingleOrDefaultAsync(ct)
                ?? throw new DomainException(ErrorCodes.NotFound, "There is no rental with this id.");
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM listings WHERE id = {listingId} FOR UPDATE", ct);
            var booking = await LockAsync(bookingId, ct);
            if (actorId is { } owner && booking.OwnerId != owner)
            {
                throw new DomainException(ErrorCodes.Forbidden, "Only the owner can accept a request.");
            }

            if (actorId is null && booking.Status == BookingStatus.Declined)
            {
                throw new DomainException(ErrorCodes.Unavailable, "Some of these days were just taken by someone else.");
            }

            booking.Confirm(now);
            db.Calendar.Add(CalendarEntry.ForBooking(booking, now));
            db.BookingEvents.Add(BookingEvent.Record(booking, BookingEventKind.Confirmed, actorId, now, actorId is null ? "Booked instantly" : "Accepted by the owner"));
            var rivals = await db.Bookings
                .Where(b => b.ListingId == booking.ListingId && b.Id != booking.Id && b.Status == BookingStatus.Requested && b.Start < booking.End && booking.Start < b.End)
                .ToListAsync(ct);
            foreach (var rival in rivals)
            {
                rival.Decline("Those days went to another renter.", now);
                db.BookingEvents.Add(BookingEvent.Record(rival, BookingEventKind.Declined, null, now, rival.Reason));
                touched.Add(rival.Id);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (ex.IsExclusionViolation())
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var booking = await LockAsync(bookingId, ct);
            if (booking.Status == BookingStatus.Requested)
            {
                booking.Decline("Those days were taken a moment earlier.", now);
                db.BookingEvents.Add(BookingEvent.Record(booking, BookingEventKind.Declined, null, now, booking.Reason));
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            await SettleAllAsync(touched, ct);
            throw new DomainException(ErrorCodes.Unavailable, "Some of these days were just taken by someone else.");
        }

        await SettleAllAsync(touched, ct);
    }

    public async Task DeclineAsync(Guid bookingId, Guid ownerId, string? reason, CancellationToken ct)
    {
        await ChangeAsync(bookingId, ct, booking =>
        {
            if (booking.OwnerId != ownerId)
            {
                throw new DomainException(ErrorCodes.Forbidden, "Only the owner can decline a request.");
            }

            booking.Decline(reason, clock.GetUtcNow());
            return BookingEvent.Record(booking, BookingEventKind.Declined, ownerId, clock.GetUtcNow(), booking.Reason);
        });
    }

    /// <summary>Either side cancels; a confirmed rental gives its days back and its refund is worked out by the policy.</summary>
    public async Task CancelAsync(Guid bookingId, Guid userId, string? reason, CancellationToken ct)
    {
        await ChangeAsync(bookingId, ct, async booking =>
        {
            var now = clock.GetUtcNow();
            var refund = booking.Cancel(userId, reason, now);
            await db.Calendar.Where(c => c.BookingId == booking.Id).ExecuteDeleteAsync(ct);
            var who = userId == booking.OwnerId ? "the owner" : "the renter";
            return BookingEvent.Record(booking, BookingEventKind.Cancelled, userId, now,
                refund > 0 ? $"Cancelled by {who}; {refund / 100m:0.00} TL back" : $"Cancelled by {who}");
        });
    }

    /// <summary>Requests the owner did not answer in time lapse, and their holds are let go.</summary>
    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.Bookings.AsNoTracking().Where(b => b.Status == BookingStatus.Requested && b.RespondBy <= now)
            .OrderBy(b => b.RespondBy).Select(b => b.Id).Take(500).ToListAsync(ct);
        foreach (var id in due)
        {
            await ChangeAsync(id, ct, booking =>
            {
                if (booking.Status != BookingStatus.Requested)
                {
                    return null;
                }

                booking.Expire(now);
                return BookingEvent.Record(booking, BookingEventKind.Expired, null, now, booking.Reason);
            });
        }

        return due.Count;
    }

    /// <summary>Bookings with money still to move: a charge not yet made, a refund owed, holds to let go.</summary>
    public async Task<int> SettleOutstandingAsync(CancellationToken ct)
    {
        var due = await db.Bookings.AsNoTracking()
            .Where(b => (b.RentChargedAt == null && b.ConfirmedAt != null)
                || (b.RefundMinor > 0 && b.RefundedAt == null)
                || (b.HoldsReleasedAt == null && (b.Status == BookingStatus.Declined || b.Status == BookingStatus.Expired || b.Status == BookingStatus.Cancelled)))
            .OrderBy(b => b.RequestedAt).Select(b => b.Id).Take(500).ToListAsync(ct);
        await SettleAllAsync(due, ct);
        return due.Count;
    }

    /// <summary>
    /// Moves the money a booking owes, in order: charge the rent, refund what is due, let go of holds. Each step is
    /// keyed at the provider, posts its journal entry under a unique key, and is marked done in the same transaction
    /// as that entry, under a lock on the booking, so two settlers cannot both do it.
    /// </summary>
    public async Task SettleAsync(Guid bookingId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var booking = await LockAsync(bookingId, ct);
        var now = clock.GetUtcNow();

        if (booking.OwesRentCharge && booking.RentHoldId is { } rentHold)
        {
            await payments.CaptureAsync(rentHold, booking.RentMinor, $"booking:{booking.Id}:capture-rent", ct);
            db.Journal.Add(JournalEntry.RentCharged(booking.Id, booking.OwnerId, booking.RentMinor, booking.FeeMinor, now));
            booking.RentCharged(now);
            db.BookingEvents.Add(BookingEvent.Record(booking, BookingEventKind.RentCharged, null, now, $"{booking.RentMinor / 100m:0.00} TL"));
        }

        if (booking.OwesRefund && booking.RentChargedAt is not null && booking.RentHoldId is { } chargedHold)
        {
            await payments.RefundAsync(chargedHold, booking.RefundMinor, $"booking:{booking.Id}:refund", ct);
            db.Journal.Add(JournalEntry.RentRefunded(booking.Id, booking.OwnerId, booking.RentMinor, booking.FeeMinor, booking.RefundMinor, now));
            booking.Refunded(now);
            db.BookingEvents.Add(BookingEvent.Record(booking, BookingEventKind.Refunded, null, now, $"{booking.RefundMinor / 100m:0.00} TL"));
        }

        if (booking.OwesRelease)
        {
            if (booking.RentHoldId is { } held && booking.RentChargedAt is null)
            {
                await payments.ReleaseAsync(held, $"booking:{booking.Id}:release-rent", ct);
            }

            if (booking.DepositHoldId is { } deposit)
            {
                await payments.ReleaseAsync(deposit, $"booking:{booking.Id}:release-deposit", ct);
            }

            booking.HoldsReleased(now);
            db.BookingEvents.Add(BookingEvent.Record(booking, BookingEventKind.HoldsReleased, null, now));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task SettleAllAsync(IEnumerable<Guid> bookingIds, CancellationToken ct)
    {
        foreach (var id in bookingIds)
        {
            try
            {
                await SettleAsync(id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The booking's state is saved; the sweeper moves the money on its next pass.
                LogSettleFailed(logger, ex, id);
            }
        }
    }

    private Task ChangeAsync(Guid bookingId, CancellationToken ct, Func<Booking, BookingEvent?> change) =>
        ChangeAsync(bookingId, ct, booking => Task.FromResult(change(booking)));

    private async Task ChangeAsync(Guid bookingId, CancellationToken ct, Func<Booking, Task<BookingEvent?>> change)
    {
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var booking = await LockAsync(bookingId, ct);
            if (await change(booking) is { } recorded)
            {
                db.BookingEvents.Add(recorded);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await SettleAllAsync([bookingId], ct);
    }

    private async Task<Booking> LockAsync(Guid bookingId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM bookings WHERE id = {bookingId} FOR UPDATE", ct);
        return await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no rental with this id.");
    }

    internal Task<bool> TakenAsync(Guid listingId, DateOnly start, DateOnly end, CancellationToken ct) =>
        db.Calendar.AnyAsync(c => c.ListingId == listingId && c.Start < end && start < c.End, ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Booking {BookingId} requested for listing {ListingId}")]
    private static partial void LogRequested(ILogger logger, Guid bookingId, Guid listingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settling booking {BookingId} failed; the sweeper will try again")]
    private static partial void LogSettleFailed(ILogger logger, Exception exception, Guid bookingId);
}
