using System.Security.Claims;
using Emanet.Core;
using Emanet.Core.Listings;
using Emanet.Infrastructure.Persistence;
using Emanet.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Emanet.Api.Features.Listings;

public sealed record PhotoOrderRequest(IReadOnlyList<Guid> PhotoIds);

internal static class PhotoEndpoints
{
    public static IEndpointRouteBuilder MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/listings/{listingId:guid}/photos").WithTags("Photos").RequireAuthorization();
        group.MapPost("/", Upload).WithSummary("Add a photo to your listing")
            .WithDescription($"""
                The body is the image itself: JPEG, PNG or WebP, at most {PhotoProcessor.MaxUploadBytes / 1024 / 1024} MB, recognised by its
                first bytes. It is turned upright, its metadata (including where it was taken) is removed, and it is stored as a
                {PhotoProcessor.FullEdge} px JPEG and a {PhotoProcessor.ThumbWidth} × {PhotoProcessor.ThumbHeight} px thumbnail. A listing has at most {ListingPhoto.MaxPerListing} photos.
                """)
            .Accepts<Stream>("image/jpeg", "image/png", "image/webp")
            .DisableAntiforgery();
        group.MapPut("/order", Reorder).WithSummary("Put your listing's photos in a new order; the first is the cover");
        group.MapDelete("/{photoId:guid}", Delete).WithSummary("Remove a photo from your listing");
        var files = app.MapGroup("/api/photos").WithTags("Photos").AllowAnonymous();
        files.MapGet("/{photoId:guid}", (Guid photoId, EmanetDbContext db, IFileStore store, CancellationToken ct) => ServeAsync(photoId, thumb: false, db, store, ct))
            .WithSummary("A listing photo, up to 1600 px");
        files.MapGet("/{photoId:guid}/thumb", (Guid photoId, EmanetDbContext db, IFileStore store, CancellationToken ct) => ServeAsync(photoId, thumb: true, db, store, ct))
            .WithSummary("A listing photo's thumbnail");
        return app;
    }

    private static async Task<Created<PhotoResponse>> Upload(
        Guid listingId, HttpRequest request, ClaimsPrincipal user, EmanetDbContext db, IFileStore files, TimeProvider clock, CancellationToken ct)
    {
        var listing = await ListingEndpoints.OwnAsync(db, listingId, user, ct);
        var count = await db.ListingPhotos.CountAsync(p => p.ListingId == listingId, ct);
        if (count >= ListingPhoto.MaxPerListing)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"A listing has at most {ListingPhoto.MaxPerListing} photos. Remove one first.");
        }

        var processed = await PhotoProcessor.ProcessAsync(await ReadAsync(request.Body, PhotoProcessor.MaxUploadBytes, ct), ct);
        var photo = ListingPhoto.Create(listing.Id, count, processed.Width, processed.Height, processed.Full.Length, clock.GetUtcNow());
        // Files first: a row never points at a file that is not there. A file without a row is never served.
        await files.PutAsync(photo.FullKey, processed.Full, "image/jpeg", ct);
        await files.PutAsync(photo.ThumbKey, processed.Thumb, "image/jpeg", ct);
        db.ListingPhotos.Add(photo);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created(ListingQueries.PhotoUrl(photo.Id),
            new PhotoResponse(photo.Id, ListingQueries.PhotoUrl(photo.Id), ListingQueries.ThumbUrl(photo.Id), photo.Width, photo.Height));
    }

    private static async Task<NoContent> Reorder(Guid listingId, PhotoOrderRequest request, ClaimsPrincipal user, EmanetDbContext db, CancellationToken ct)
    {
        await ListingEndpoints.OwnAsync(db, listingId, user, ct);
        var photos = await db.ListingPhotos.Where(p => p.ListingId == listingId).ToListAsync(ct);
        if (request.PhotoIds.Count != photos.Count || request.PhotoIds.Distinct().Count() != photos.Count || photos.Any(p => !request.PhotoIds.Contains(p.Id)))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Send every photo of the listing exactly once.");
        }

        foreach (var photo in photos)
        {
            photo.MoveTo(request.PhotoIds.ToList().IndexOf(photo.Id));
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Delete(Guid listingId, Guid photoId, ClaimsPrincipal user, EmanetDbContext db, IFileStore files, CancellationToken ct)
    {
        await ListingEndpoints.OwnAsync(db, listingId, user, ct);
        var photos = await db.ListingPhotos.Where(p => p.ListingId == listingId).OrderBy(p => p.Position).ToListAsync(ct);
        var photo = photos.SingleOrDefault(p => p.Id == photoId) ?? throw new DomainException(ErrorCodes.NotFound, "This listing has no photo with this id.");
        db.ListingPhotos.Remove(photo);
        var position = 0;
        foreach (var rest in photos.Where(p => p.Id != photoId))
        {
            rest.MoveTo(position++);
        }

        await db.SaveChangesAsync(ct);
        // The row is gone, so the files are never served again; removing them is tidying up.
        await files.DeleteAsync(photo.FullKey, ct);
        await files.DeleteAsync(photo.ThumbKey, ct);
        return TypedResults.NoContent();
    }

    /// <summary>Photos of listings that are not removed. They never change once stored, so browsers may keep them.</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> ServeAsync(Guid photoId, bool thumb, EmanetDbContext db, IFileStore store, CancellationToken ct)
    {
        var photo = await (
            from p in db.ListingPhotos.AsNoTracking()
            join l in db.Listings on p.ListingId equals l.Id
            where p.Id == photoId && l.Status != ListingStatus.Removed
            select p).SingleOrDefaultAsync(ct);
        var bytes = photo is null ? null : await store.GetAsync(thumb ? photo.ThumbKey : photo.FullKey, ct);
        if (bytes is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.File(bytes, "image/jpeg", entityTag: new EntityTagHeaderValue($"\"{photo!.Id:N}{(thumb ? "t" : "f")}\""));
    }

    internal static async Task<byte[]> ReadAsync(Stream body, long max, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (copy.Length + read > max)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, $"A photo is at most {max / 1024 / 1024} MB.");
            }

            copy.Write(buffer, 0, read);
        }

        return copy.Length > 0 ? copy.ToArray() : throw new DomainException(ErrorCodes.ValidationFailed, "The photo is empty.");
    }
}
