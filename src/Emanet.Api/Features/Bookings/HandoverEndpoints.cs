using System.Security.Claims;
using Emanet.Api.Common;
using Emanet.Api.Features.Listings;
using Emanet.Core;
using Emanet.Core.Bookings;
using Emanet.Core.Reviews;
using Emanet.Infrastructure.Persistence;
using Emanet.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Emanet.Api.Features.Bookings;

/// <summary>Pickup and return, condition photos, damage claims and reviews.</summary>
internal static class HandoverEndpoints
{
    public static IEndpointRouteBuilder MapHandoverEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings/{bookingId:guid}").WithTags("Handover").RequireAuthorization();
        group.MapPost("/pickup", PickUp).WithSummary("Hand the item over (owner), with the code on the renter's screen")
            .WithDescription($"From the first day of the rental. After {Booking.CodeTries} wrong codes the renter gets a new one.");
        group.MapPost("/return", GiveBack).WithSummary("Give the item back (renter), with the code on the owner's screen");
        group.MapPost("/all-good", AllGood).WithSummary("Say the item came back fine (owner); the deposit is let go")
            .WithDescription("Without this or a damage report, the deposit is let go 48 hours after the return.");
        group.MapPost("/claim", Claim).WithSummary("Report damage within 48 hours of the return (owner), for up to the deposit");
        group.MapPost("/claim/accept", AcceptClaim).WithSummary("Agree to the damage claim (renter); that much of the deposit is taken");
        group.MapPost("/claim/reject", RejectClaim).WithSummary("Disagree with the damage claim (renter); an administrator decides")
            .WithDescription("A claim not answered within 72 hours is taken as accepted.");
        group.MapPost("/review", Review).WithSummary("Review the other side, once, in the two weeks after the rental");
        group.MapPost("/photos", Upload).WithSummary("Add a condition photo: stage=Pickup, Return or Damage")
            .WithDescription($"""
                Either side adds pickup photos (confirmed or picked up) and return photos (picked up or returned); the owner
                adds damage photos while a claim can be made or is open. At most {HandoverPhoto.MaxPerStage} per stage.
                Like listing photos they are turned upright and stripped of metadata, including where they were taken.
                """)
            .Accepts<Stream>("image/jpeg", "image/png", "image/webp")
            .DisableAntiforgery();
        group.MapGet("/photos/{photoId:guid}", (Guid bookingId, Guid photoId, HttpContext http, EmanetDbContext db, IFileStore store, IConfiguration configuration, CancellationToken ct) =>
            ServeAsync(bookingId, photoId, thumb: false, http, db, store, configuration, ct)).WithSummary("A condition photo, for the two sides and administrators");
        group.MapGet("/photos/{photoId:guid}/thumb", (Guid bookingId, Guid photoId, HttpContext http, EmanetDbContext db, IFileStore store, IConfiguration configuration, CancellationToken ct) =>
            ServeAsync(bookingId, photoId, thumb: true, http, db, store, configuration, ct)).WithSummary("A condition photo's thumbnail");

        app.MapGet("/api/listings/{listingId:guid}/reviews", ListingReviews).WithTags("Reviews").AllowAnonymous().WithSummary("What renters said about a listing");
        app.MapGet("/api/users/{userId:guid}/reviews", RenterReviews).WithTags("Reviews").RequireAuthorization().WithSummary("What owners said about someone as a renter");
        return app;
    }

    private static Task<Ok<BookingResponse>> PickUp(Guid bookingId, CodeRequest request, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct) =>
        Do(bookingId, user, db, clock, ct, () => bookings.PickUpAsync(bookingId, user.UserId(), request.Code, ct));

    private static Task<Ok<BookingResponse>> GiveBack(Guid bookingId, CodeRequest request, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct) =>
        Do(bookingId, user, db, clock, ct, () => bookings.GiveBackAsync(bookingId, user.UserId(), request.Code, ct));

    private static Task<Ok<BookingResponse>> AllGood(Guid bookingId, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct) =>
        Do(bookingId, user, db, clock, ct, () => bookings.AllGoodAsync(bookingId, user.UserId(), ct));

    private static Task<Ok<BookingResponse>> Claim(Guid bookingId, ClaimRequest request, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct) =>
        Do(bookingId, user, db, clock, ct, () => bookings.ReportDamageAsync(bookingId, user.UserId(), Money.ToMinor(request.Amount, "amount"), request.Note, ct));

    private static Task<Ok<BookingResponse>> AcceptClaim(Guid bookingId, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct) =>
        Do(bookingId, user, db, clock, ct, () => bookings.AcceptClaimAsync(bookingId, user.UserId(), ct));

    private static Task<Ok<BookingResponse>> RejectClaim(Guid bookingId, AnswerRequest request, ClaimsPrincipal user, BookingService bookings, EmanetDbContext db, TimeProvider clock, CancellationToken ct) =>
        Do(bookingId, user, db, clock, ct, () => bookings.RejectClaimAsync(bookingId, user.UserId(), request.Note, ct));

    private static async Task<Ok<BookingResponse>> Do(Guid bookingId, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct, Func<Task> change)
    {
        await BookingQueries.FindAsync(db, bookingId, user.UserId(), ct);
        await change();
        return TypedResults.Ok(await BookingQueries.DetailAsync(db, bookingId, user.UserId(), clock.GetUtcNow(), ct));
    }

    /// <summary>
    /// The review, the event and the reviewed side's rating change together. Ratings are added in place, so two
    /// reviews of one listing at the same moment both count.
    /// </summary>
    private static async Task<Created<ReviewResponse>> Review(Guid bookingId, ReviewRequest request, ClaimsPrincipal user, EmanetDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var me = user.UserId();
        var booking = await BookingQueries.FindAsync(db, bookingId, me, ct);
        var now = clock.GetUtcNow();
        var review = Core.Reviews.Review.Write(booking, me, request.Stars, request.Comment, now);
        if (await db.Reviews.AnyAsync(r => r.BookingId == bookingId && r.AuthorId == me, ct))
        {
            throw new DomainException(ErrorCodes.AlreadyReviewed, "You already reviewed this rental.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Reviews.Add(review);
        db.BookingEvents.Add(BookingEvent.Record(booking, BookingEventKind.Reviewed, me, now, $"{review.Stars} of 5"));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_reviews_booking_id_author_id"))
        {
            throw new DomainException(ErrorCodes.AlreadyReviewed, "You already reviewed this rental.");
        }

        if (review.Subject == ReviewSubject.Listing)
        {
            await db.Listings.Where(l => l.Id == review.ListingId)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.RatingSum, l => l.RatingSum + review.Stars).SetProperty(l => l.RatingCount, l => l.RatingCount + 1), ct);
        }
        else
        {
            await db.Users.Where(x => x.Id == review.SubjectUserId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RatingSum, x => x.RatingSum + review.Stars).SetProperty(x => x.RatingCount, x => x.RatingCount + 1), ct);
        }

        await transaction.CommitAsync(ct);
        var author = await db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.DisplayName).SingleAsync(ct);
        return TypedResults.Created($"/api/bookings/{bookingId}", new ReviewResponse(review.Id, author, review.Stars, review.Comment, review.CreatedAt));
    }

    private static Task<Ok<Page<ReviewResponse>>> ListingReviews(Guid listingId, int? page, int? pageSize, EmanetDbContext db, CancellationToken ct) =>
        ReviewsAsync(db.Reviews.Where(r => r.ListingId == listingId && r.Subject == ReviewSubject.Listing), db, page, pageSize, ct);

    private static Task<Ok<Page<ReviewResponse>>> RenterReviews(Guid userId, int? page, int? pageSize, EmanetDbContext db, CancellationToken ct) =>
        ReviewsAsync(db.Reviews.Where(r => r.SubjectUserId == userId && r.Subject == ReviewSubject.Renter), db, page, pageSize, ct);

    private static async Task<Ok<Page<ReviewResponse>>> ReviewsAsync(IQueryable<Review> query, EmanetDbContext db, int? page, int? pageSize, CancellationToken ct)
    {
        var (number, size) = Paging.Clamp(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.AsNoTracking().OrderByDescending(r => r.CreatedAt).Skip((number - 1) * size).Take(size)
            .Select(r => new ReviewResponse(r.Id, db.Users.Where(u => u.Id == r.AuthorId).Select(u => u.DisplayName).First(), r.Stars, r.Comment, r.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new Page<ReviewResponse>(items, total, number, size));
    }

    private static async Task<Created<HandoverPhotoResponse>> Upload(
        Guid bookingId, HandoverStage stage, HttpRequest request, ClaimsPrincipal user, EmanetDbContext db, IFileStore files, TimeProvider clock, CancellationToken ct)
    {
        var me = user.UserId();
        var booking = await BookingQueries.FindAsync(db, bookingId, me, ct);
        var count = await db.HandoverPhotos.CountAsync(p => p.BookingId == bookingId && p.Stage == stage, ct);
        if (count >= HandoverPhoto.MaxPerStage)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"A rental has at most {HandoverPhoto.MaxPerStage} {stage.ToString().ToLowerInvariant()} photos.");
        }

        // The rules first, so a photo that cannot be added is not processed and stored for nothing.
        HandoverPhoto.Take(booking, stage, me, 1, 1, clock.GetUtcNow());
        var processed = await PhotoProcessor.ProcessAsync(await PhotoEndpoints.ReadAsync(request.Body, PhotoProcessor.MaxUploadBytes, ct), ct);
        var photo = HandoverPhoto.Take(booking, stage, me, processed.Width, processed.Height, clock.GetUtcNow());
        await files.PutAsync(photo.FullKey, processed.Full, "image/jpeg", ct);
        await files.PutAsync(photo.ThumbKey, processed.Thumb, "image/jpeg", ct);
        db.HandoverPhotos.Add(photo);
        await db.SaveChangesAsync(ct);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.DisplayName).SingleAsync(ct);
        var url = BookingQueries.PhotoUrl(bookingId, photo.Id);
        return TypedResults.Created(url, new HandoverPhotoResponse(photo.Id, photo.Stage, name, url, url + "/thumb", photo.Width, photo.Height, photo.CreatedAt));
    }

    /// <summary>Condition photos are private: the response is never cached by shared caches.</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> ServeAsync(
        Guid bookingId, Guid photoId, bool thumb, HttpContext http, EmanetDbContext db, IFileStore store, IConfiguration configuration, CancellationToken ct)
    {
        var user = http.User;
        await BookingQueries.FindAsync(db, bookingId, user.UserId(), ct, admin: await user.IsAdminAsync(db, configuration, ct));
        var photo = await db.HandoverPhotos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == photoId && p.BookingId == bookingId, ct);
        var bytes = photo is null ? null : await store.GetAsync(thumb ? photo.ThumbKey : photo.FullKey, ct);
        if (bytes is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.CacheControl = "private, max-age=3600";
        return TypedResults.File(bytes, "image/jpeg", entityTag: new EntityTagHeaderValue($"\"{photo!.Id:N}{(thumb ? "t" : "f")}\""));
    }
}
