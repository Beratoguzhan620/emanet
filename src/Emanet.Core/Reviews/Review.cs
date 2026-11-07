using Emanet.Core.Bookings;

namespace Emanet.Core.Reviews;

/// <summary>Who a review is about: the item and its owner, written by the renter, or the renter, written by the owner.</summary>
public enum ReviewSubject
{
    Listing,
    Renter,
}

/// <summary>One side's word on a finished rental: one to five stars and, if they like, a few lines.</summary>
public sealed class Review
{
    public const int CommentMax = 500;

    /// <summary>Reviews can be written for this long after a rental ends.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private Review()
    {
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public Guid ListingId { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>The person the review is about: the owner for a listing review, the renter otherwise.</summary>
    public Guid SubjectUserId { get; private set; }

    public ReviewSubject Subject { get; private set; }

    public int Stars { get; private set; }

    public string Comment { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Review Write(Booking booking, Guid authorId, int stars, string? comment, DateTimeOffset now)
    {
        if (!booking.InvolvesUser(authorId))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no rental with this id.");
        }

        if (!booking.CanBeReviewed(now))
        {
            throw new DomainException(ErrorCodes.InvalidState, "Reviews are written in the two weeks after a rental ends.");
        }

        if (stars is < 1 or > 5)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Give one to five stars.");
        }

        var byRenter = authorId == booking.RenterId;
        return new Review
        {
            Id = Guid.CreateVersion7(now),
            BookingId = booking.Id,
            ListingId = booking.ListingId,
            AuthorId = authorId,
            SubjectUserId = byRenter ? booking.OwnerId : booking.RenterId,
            Subject = byRenter ? ReviewSubject.Listing : ReviewSubject.Renter,
            Stars = stars,
            Comment = Text.CleanParagraphs(comment, CommentMax, "A comment"),
            CreatedAt = now,
        };
    }
}
