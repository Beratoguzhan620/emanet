using System.Security.Claims;
using Emanet.Api.Common;
using Emanet.Core;
using Emanet.Core.Listings;
using Emanet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Features.Listings;

internal static class ListingEndpoints
{
    public static IEndpointRouteBuilder MapListingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/listings").WithTags("Listings");
        group.MapGet("/", Search).AllowAnonymous().WithSummary("Search what is for rent")
            .WithDescription("No sign-in needed. Words are matched as Turkish (so 'sandalyeler' finds 'sandalye') and as the start of a word. " +
                "With lat and lng, results are limited to radiusKm (25 by default) and distances are measured from each item's approximate place. " +
                "With from and to, only listings free for all of those days are shown.");
        group.MapGet("/categories", Categories).AllowAnonymous().WithSummary("The categories, with how many active listings each has");
        group.MapGet("/{listingId:guid}", Get).AllowAnonymous().WithSummary("One listing")
            .WithDescription("Its owner, and a renter with a confirmed rental of it, see the exact pickup point; everyone else sees the centre of a cell a few hundred metres wide.");
        group.MapPost("/", Create).RequireAuthorization().WithSummary("Rent something out");
        group.MapPut("/{listingId:guid}", Update).RequireAuthorization().WithSummary("Change your listing");
        group.MapPost("/{listingId:guid}/pause", Pause).RequireAuthorization().WithSummary("Hide your listing from search for a while");
        group.MapPost("/{listingId:guid}/resume", Resume).RequireAuthorization().WithSummary("Show your listing in search again");
        group.MapDelete("/{listingId:guid}", Remove).RequireAuthorization().WithSummary("Take your listing down for good");
        app.MapGet("/api/me/listings", Mine).WithTags("Listings").RequireAuthorization().WithSummary("Everything you rent out, paused listings included");
        return app;
    }

    private static async Task<Ok<Page<ListingSummary>>> Search([AsParameters] SearchFilters filters, EmanetDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ListingQueries.SearchAsync(db, filters, ct));

    private static async Task<Ok<List<CategoryResponse>>> Categories(EmanetDbContext db, CancellationToken ct)
    {
        var counts = await db.Listings.AsNoTracking().Where(l => l.Status == ListingStatus.Active)
            .GroupBy(l => l.Category).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return TypedResults.Ok(Enum.GetValues<Category>().Select(c => new CategoryResponse(c, counts.GetValueOrDefault(c))).ToList());
    }

    private static async Task<Ok<ListingResponse>> Get(Guid listingId, ClaimsPrincipal user, EmanetDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ListingQueries.DetailAsync(db, listingId, user.Identity?.IsAuthenticated == true ? user.UserId() : null, ct));

    private static async Task<Ok<List<ListingSummary>>> Mine(ClaimsPrincipal user, EmanetDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ListingQueries.MineAsync(db, user.UserId(), ct));

    private static async Task<Created<ListingResponse>> Create(ListingRequest request, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var userId = user.UserId();
        var listing = Listing.Create(
            userId, request.Title, request.Description, request.Category, Money.ToMinor(request.PricePerDay, "The price per day"), request.WeeklyPercentOff,
            Money.ToMinor(request.Deposit, "The deposit"), request.Latitude, request.Longitude, request.Area, request.MinDays, request.MaxDays, clock.GetUtcNow(), request.InstantBook);
        db.Listings.Add(listing);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/listings/{listing.Id}", await ListingQueries.DetailAsync(db, listing.Id, userId, ct));
    }

    private static async Task<Ok<ListingResponse>> Update(Guid listingId, ListingRequest request, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var listing = await OwnAsync(db, listingId, user, ct);
        listing.Change(
            request.Title, request.Description, request.Category, Money.ToMinor(request.PricePerDay, "The price per day"), request.WeeklyPercentOff,
            Money.ToMinor(request.Deposit, "The deposit"), request.Latitude, request.Longitude, request.Area, request.MinDays, request.MaxDays, clock.GetUtcNow(), request.InstantBook);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ListingQueries.DetailAsync(db, listingId, listing.OwnerId, ct));
    }

    private static async Task<Ok<ListingResponse>> Pause(Guid listingId, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var listing = await OwnAsync(db, listingId, user, ct);
        listing.Pause(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ListingQueries.DetailAsync(db, listingId, listing.OwnerId, ct));
    }

    private static async Task<Ok<ListingResponse>> Resume(Guid listingId, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var listing = await OwnAsync(db, listingId, user, ct);
        listing.Resume(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ListingQueries.DetailAsync(db, listingId, listing.OwnerId, ct));
    }

    private static async Task<NoContent> Remove(Guid listingId, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var listing = await OwnAsync(db, listingId, user, ct);
        listing.Remove(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>A listing the signed-in user owns. Someone else's answers 403; one that does not exist, 404.</summary>
    internal static async Task<Listing> OwnAsync(EmanetDbContext db, Guid listingId, ClaimsPrincipal user, CancellationToken ct)
    {
        var listing = await db.Listings.SingleOrDefaultAsync(l => l.Id == listingId && l.Status != ListingStatus.Removed, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no listing with this id.");
        return listing.IsOwnedBy(user.UserId()) ? listing : throw new DomainException(ErrorCodes.NotYourListing, "Only the owner can change this listing.");
    }
}
