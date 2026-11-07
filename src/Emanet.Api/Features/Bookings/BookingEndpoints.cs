using System.Security.Claims;
using Emanet.Api.Common;
using Emanet.Api.Features.Listings;
using Emanet.Core;
using Emanet.Core.Bookings;
using Emanet.Core.Listings;
using Emanet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Features.Bookings;

internal static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").WithTags("Rentals").RequireAuthorization();
        group.MapPost("/", Request).WithSummary("Ask to rent something for some days")
            .WithDescription("""
                The rent and the deposit are held on the card straight away. The owner has 24 hours to accept; until then the
                days stay open to others. Listings with instant booking confirm at once. When two people ask for the same
                days at the same moment, the database lets exactly one have them; the other gets 409 `unavailable` and
                their holds are let go.
                """);
        group.MapGet("/{bookingId:guid}", Get).WithSummary("One of your rentals, as owner or renter");
        group.MapPost("/{bookingId:guid}/accept", Accept).WithSummary("Accept a request for your listing; the rent is charged");
        group.MapPost("/{bookingId:guid}/decline", Decline).WithSummary("Decline a request for your listing; the holds are let go");
        group.MapPost("/{bookingId:guid}/cancel", Cancel).WithSummary("Cancel a request or a confirmed rental")
            .WithDescription("Owners refund the whole rent. Renters get it all back up to 48 hours before the pickup day, half after that. The deposit hold is always let go.");
        app.MapGet("/api/me/bookings", Mine).WithTags("Rentals").RequireAuthorization().WithSummary("Your rentals: role=renter, role=owner, or both");

        var listings = app.MapGroup("/api/listings/{listingId:guid}").WithTags("Calendar");
        listings.MapGet("/quote", Quote).AllowAnonymous().WithSummary("What some days would cost, and whether they are free");
        listings.MapGet("/calendar", Calendar).AllowAnonymous().WithSummary("Which days are taken, from a date for up to 180 days")
            .WithDescription("Everyone sees which days are booked or closed; only the owner sees which is which and their notes.");
        listings.MapPost("/blocks", Block).RequireAuthorization().WithSummary("Close days on your listing's calendar");
        listings.MapDelete("/blocks/{entryId:guid}", Unblock).RequireAuthorization().WithSummary("Open days you closed");
        return app;
    }

    private static async Task<Created<BookingResponse>> Request(BookingRequest request, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var booking = await bookings.RequestAsync(user.UserId(), request.ListingId, request.Start, request.End, request.Message, request.PaymentMethod, ct);
        return TypedResults.Created($"/api/bookings/{booking.Id}", await BookingQueries.DetailAsync(db, booking.Id, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<BookingResponse>> Get(Guid bookingId, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct) =>
        TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));

    private static async Task<Ok<BookingResponse>> Accept(Guid bookingId, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await BookingQueries.FindAsync(db, bookingId, user.UserId(), ct);
        await bookings.ConfirmAsync(bookingId, user.UserId(), ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<BookingResponse>> Decline(Guid bookingId, ReasonRequest request, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await BookingQueries.FindAsync(db, bookingId, user.UserId(), ct);
        await bookings.DeclineAsync(bookingId, user.UserId(), request.Reason, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<BookingResponse>> Cancel(Guid bookingId, ReasonRequest request, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await BookingQueries.FindAsync(db, bookingId, user.UserId(), ct);
        await bookings.CancelAsync(bookingId, user.UserId(), request.Reason, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<Page<BookingSummary>>> Mine(string? role, string? status, int? page, int? pageSize, ClaimsPrincipal user, EmanetDbContext db, CancellationToken ct)
    {
        BookingStatus[]? statuses = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse<BookingStatus>(s, ignoreCase: true, out var parsed) ? parsed : throw new DomainException(ErrorCodes.ValidationFailed, $"'{s}' is not a status."))
                .ToArray();
        }

        if (role is not (null or "owner" or "renter"))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "role is owner or renter.");
        }

        return TypedResults.Ok(await BookingQueries.MineAsync(db, user.UserId(), role, statuses, page, pageSize, ct));
    }

    private static async Task<Ok<QuoteResponse>> Quote(Guid listingId, DateOnly start, DateOnly end, EmanetDbContext db, BookingService bookings, TimeProvider clock, CancellationToken ct)
    {
        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId && l.Status != ListingStatus.Removed, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no listing with this id.");
        Booking.CheckDates(start, end, Clock.Today(clock.GetUtcNow()));
        var days = end.DayNumber - start.DayNumber;
        var rent = listing.RentFor(days);
        var full = listing.PricePerDayMinor * days;
        var taken = await bookings.TakenAsync(listingId, start, end, ct);
        var reason = listing.Status != ListingStatus.Active ? "This listing is not taking requests right now." : taken ? "Some of these days are taken." : null;
        return TypedResults.Ok(new QuoteResponse(start, end, days, Money.FromMinor(listing.PricePerDayMinor), Money.FromMinor(full), Money.FromMinor(full - rent),
            Money.FromMinor(rent), Money.FromMinor(listing.DepositMinor), reason is null, reason, listing.InstantBook));
    }

    private static async Task<Ok<List<CalendarRange>>> Calendar(Guid listingId, DateOnly? from, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listingId && l.Status != ListingStatus.Removed, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no listing with this id.");
        var start = from ?? Clock.Today(clock.GetUtcNow());
        var until = start.AddDays(180);
        var owner = user.Identity?.IsAuthenticated == true && listing.IsOwnedBy(user.UserId());
        var entries = await db.Calendar.AsNoTracking().Where(c => c.ListingId == listingId && c.End > start && c.Start < until).OrderBy(c => c.Start).ToListAsync(ct);
        return TypedResults.Ok(entries.Select(c => owner
            ? new CalendarRange(c.Id, c.Start, c.End, c.BookingId is null ? "Closed" : "Booked", c.BookingId is null ? c.Note : null)
            : new CalendarRange(null, c.Start, c.End, "Taken", null)).ToList());
    }

    private static async Task<Created<CalendarRange>> Block(Guid listingId, BlockRequest request, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var listing = await ListingEndpoints.OwnAsync(db, listingId, user, ct);
        var now = clock.GetUtcNow();
        var entry = CalendarEntry.Block(listing.Id, request.Start, request.End, request.Note, Clock.Today(now), now);
        db.Calendar.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsExclusionViolation())
        {
            throw new DomainException(ErrorCodes.Unavailable, "Some of these days are already booked or closed.");
        }

        return TypedResults.Created($"/api/listings/{listingId}/calendar", new CalendarRange(entry.Id, entry.Start, entry.End, "Closed", entry.Note));
    }

    private static async Task<NoContent> Unblock(Guid listingId, Guid entryId, ClaimsPrincipal user, EmanetDbContext db, CancellationToken ct)
    {
        await ListingEndpoints.OwnAsync(db, listingId, user, ct);
        var removed = await db.Calendar.Where(c => c.Id == entryId && c.ListingId == listingId && c.BookingId == null).ExecuteDeleteAsync(ct);
        return removed == 1 ? TypedResults.NoContent() : throw new DomainException(ErrorCodes.NotFound, "There are no closed days with this id.");
    }
}
