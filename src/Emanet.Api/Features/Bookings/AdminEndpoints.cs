using System.Security.Claims;
using Emanet.Api.Common;
using Emanet.Core.Bookings;
using Emanet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Features.Bookings;

/// <summary>Damage claims the two sides could not settle.</summary>
internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization();
        group.MapGet("/disputes", Disputes).WithSummary("Open damage claims, the ones the renter rejected first");
        group.MapGet("/bookings/{bookingId:guid}", Get).WithSummary("A rental with its photos and history, as neither side");
        group.MapPost("/bookings/{bookingId:guid}/resolve", Resolve).WithSummary("Decide a claim: take from nothing to the claimed amount from the deposit");
        return app;
    }

    private static async Task<Ok<List<DisputeSummary>>> Disputes(ClaimsPrincipal user, EmanetDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        await user.RequireAdminAsync(db, configuration, ct);
        var rows = await db.Bookings.AsNoTracking().Where(b => b.Status == BookingStatus.Disputed)
            .OrderBy(b => b.RenterAnswer == null).ThenBy(b => b.ClaimedAt).Take(200)
            .Select(b => new
            {
                Booking = b,
                Title = db.Listings.Where(l => l.Id == b.ListingId).Select(l => l.Title).First(),
                Owner = db.Users.Where(u => u.Id == b.OwnerId).Select(u => u.DisplayName).First(),
                Renter = db.Users.Where(u => u.Id == b.RenterId).Select(u => u.DisplayName).First(),
                Photos = db.HandoverPhotos.Count(p => p.BookingId == b.Id),
            })
            .ToListAsync(ct);
        return TypedResults.Ok(rows.Select(r => new DisputeSummary(
            r.Booking.Id, r.Title, new PartyResponse(r.Booking.OwnerId, r.Owner), new PartyResponse(r.Booking.RenterId, r.Renter),
            Money.FromMinor(r.Booking.DepositMinor), Money.FromMinor(r.Booking.ClaimMinor), r.Booking.ClaimNote, r.Booking.ClaimedAt!.Value,
            r.Booking.RenterAnswer, r.Booking.RenterAnswerNote, r.Photos)).ToList());
    }

    private static async Task<Ok<BookingResponse>> Get(Guid bookingId, ClaimsPrincipal user, EmanetDbContext db, IConfiguration configuration, TimeProvider clock, CancellationToken ct)
    {
        await user.RequireAdminAsync(db, configuration, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct, admin: true));
    }

    private static async Task<Ok<BookingResponse>> Resolve(
        Guid bookingId, ResolveRequest request, ClaimsPrincipal user, EmanetDbContext db, BookingService bookings, IConfiguration configuration, TimeProvider clock, CancellationToken ct)
    {
        await user.RequireAdminAsync(db, configuration, ct);
        await BookingQueries.FindAsync(db, bookingId, user.UserId(), ct, admin: true);
        await bookings.ResolveAsync(bookingId, user.UserId(), Money.ToMinor(request.Amount, "amount"), request.Note, ct);
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct, admin: true));
    }
}
