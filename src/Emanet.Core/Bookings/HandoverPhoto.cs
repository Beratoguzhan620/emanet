namespace Emanet.Core.Bookings;

/// <summary>When a condition photo was taken.</summary>
public enum HandoverStage
{
    Pickup,
    Return,
    Damage,
}

/// <summary>
/// A photo of the item's condition, taken by either side at pickup or return, or by the owner to show damage. Only the
/// two of them, and administrators deciding a claim, can see it.
/// </summary>
public sealed class HandoverPhoto
{
    public const int MaxPerStage = 8;

    private HandoverPhoto()
    {
    }

    public Guid Id { get; private set; }

    public Guid BookingId { get; private set; }

    public HandoverStage Stage { get; private set; }

    public Guid UploaderId { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string FullKey => $"bookings/{BookingId}/{Id}.jpg";

    public string ThumbKey => $"bookings/{BookingId}/{Id}-thumb.jpg";

    public static HandoverPhoto Take(Booking booking, HandoverStage stage, Guid uploaderId, int width, int height, DateTimeOffset now)
    {
        if (!booking.InvolvesUser(uploaderId))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no rental with this id.");
        }

        var open = stage switch
        {
            HandoverStage.Pickup => booking.Status is BookingStatus.Confirmed or BookingStatus.Active,
            HandoverStage.Return => booking.Status is BookingStatus.Active or BookingStatus.Returned,
            HandoverStage.Damage => uploaderId == booking.OwnerId && booking.Status is BookingStatus.Returned or BookingStatus.Disputed,
            _ => false,
        };
        if (!open)
        {
            throw new DomainException(ErrorCodes.InvalidState, stage == HandoverStage.Damage
                ? "The owner adds damage photos after the item comes back, while a claim can be made."
                : $"{stage} photos cannot be added to this rental now.");
        }

        return new HandoverPhoto { Id = Guid.CreateVersion7(now), BookingId = booking.Id, Stage = stage, UploaderId = uploaderId, Width = width, Height = height, CreatedAt = now };
    }
}
