namespace Emanet.Core;

/// <summary>Rental days are Turkish calendar days. Turkey has kept UTC+3 all year since 2016, so no time zone database is needed.</summary>
public static class Clock
{
    public static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(TurkeyOffset).DateTime);

    /// <summary>Midnight at the start of a day, Turkish time.</summary>
    public static DateTimeOffset StartOf(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TurkeyOffset);
}
