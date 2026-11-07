namespace Emanet.Core.Bookings;

/// <summary>
/// Days a listing is taken, from <see cref="Start"/> (the pickup day) up to but not including <see cref="End"/> (the
/// return day), so one rental can come back the morning the next one starts. Either a confirmed rental or days the
/// owner closed. The database refuses two entries for the same listing that share a day.
/// </summary>
public sealed class CalendarEntry
{
    public const int NoteMax = 200;

    private CalendarEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid ListingId { get; private set; }

    public DateOnly Start { get; private set; }

    public DateOnly End { get; private set; }

    /// <summary>The rental holding these days, or null for days the owner closed.</summary>
    public Guid? BookingId { get; private set; }

    public string Note { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static CalendarEntry ForBooking(Booking booking, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), ListingId = booking.ListingId, Start = booking.Start, End = booking.End, BookingId = booking.Id, CreatedAt = now };

    public static CalendarEntry Block(Guid listingId, DateOnly start, DateOnly end, string? note, DateOnly today, DateTimeOffset now)
    {
        Booking.CheckDates(start, end, today);
        return new()
        {
            Id = Guid.CreateVersion7(now), ListingId = listingId, Start = start, End = end, Note = Text.CleanParagraphs(note, NoteMax, "A note"), CreatedAt = now,
        };
    }
}
