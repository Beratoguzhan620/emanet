using Emanet.Core.Listings;

namespace Emanet.UnitTests;

internal static class Builders
{
    public static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid Owner = Guid.NewGuid();

    /// <summary>A drill in Atakum: 150 TL a day, 20 percent off a week or more, 1,000 TL deposit, 1 to 30 days.</summary>
    public static Listing Drill(int weekly = 20, int minDays = 1, int maxDays = 30) =>
        Listing.Create(Owner, "Darbeli matkap, Bosch", "Çantası ile.", Category.Tools, 150_00, weekly, 1000_00, 41.333512, 36.271438, "Atakum, Samsun", minDays, maxDays, Now);
}
