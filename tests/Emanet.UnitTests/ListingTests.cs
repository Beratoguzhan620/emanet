using Emanet.Core;
using Emanet.Core.Listings;

namespace Emanet.UnitTests;

public sealed class ListingTests
{
    [Fact]
    public void A_listing_keeps_its_exact_place_and_a_public_one()
    {
        var drill = Builders.Drill();

        Assert.Equal((41.333512, 36.271438), (drill.Location.Y, drill.Location.X));
        Assert.Equal((41.3325, 36.2725), (drill.PublicLocation.Y, drill.PublicLocation.X));
        Assert.Equal(ListingStatus.Active, drill.Status);
    }

    [Theory]
    [InlineData(1, 150_00)]
    [InlineData(6, 900_00)]
    [InlineData(7, 840_00)]
    [InlineData(30, 3600_00)]
    public void Rent_is_the_days_times_the_price_with_the_weekly_discount_from_seven_days(int days, long rent) =>
        Assert.Equal(rent, Builders.Drill().RentFor(days));

    [Fact]
    public void Rent_rounds_the_discount_to_the_nearest_kurus()
    {
        var odd = Listing.Create(Builders.Owner, "Çadır, dört kişilik", null, Category.Camping, 333, 15, 0, 41.3, 36.3, "Atakum", 1, 30, Builders.Now);

        Assert.Equal(1981, odd.RentFor(7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Rent_is_only_for_the_days_the_owner_allows(int days)
    {
        var error = Assert.Throws<DomainException>(() => Builders.Drill().RentFor(days));

        Assert.Equal("This item is rented for 1 to 30 days.", error.Message);
    }

    [Theory]
    [InlineData("Matk", 150_00, 0, 1000_00, 1, 30)]
    [InlineData("Darbeli matkap", 99, 0, 1000_00, 1, 30)]
    [InlineData("Darbeli matkap", 150_00, 51, 1000_00, 1, 30)]
    [InlineData("Darbeli matkap", 150_00, 0, -1, 1, 30)]
    [InlineData("Darbeli matkap", 150_00, 0, 1000_00, 0, 30)]
    [InlineData("Darbeli matkap", 150_00, 0, 1000_00, 5, 4)]
    [InlineData("Darbeli matkap", 150_00, 0, 1000_00, 1, 91)]
    public void Listings_are_checked(string title, long price, int weekly, long deposit, int min, int max) =>
        Assert.Throws<DomainException>(() =>
            Listing.Create(Builders.Owner, title, null, Category.Tools, price, weekly, deposit, 41.3, 36.3, "Atakum", min, max, Builders.Now));

    [Fact]
    public void Text_is_tidied()
    {
        var listing = Listing.Create(Builders.Owner, "  Darbeli   matkap  ", null, Category.Tools, 150_00, 0, 0, 41.3, 36.3, " Atakum,  Samsun ", 1, 1, Builders.Now);

        Assert.Equal(("Darbeli matkap", "Atakum, Samsun"), (listing.Title, listing.Area));
    }

    [Fact]
    public void A_removed_listing_cannot_come_back_or_change()
    {
        var drill = Builders.Drill();
        drill.Pause(Builders.Now);
        drill.Resume(Builders.Now);
        drill.Remove(Builders.Now);

        Assert.Throws<DomainException>(() => drill.Resume(Builders.Now));
        Assert.Throws<DomainException>(() => drill.Change("Başka bir şey", null, Category.Tools, 100_00, 0, 0, 41.3, 36.3, "Atakum", 1, 2, Builders.Now));
    }

    [Fact]
    public void Ratings_average_to_one_decimal()
    {
        var drill = Builders.Drill();
        Assert.Null(drill.Rating);

        drill.Rate(5);
        drill.Rate(4);
        drill.Rate(4);

        Assert.Equal((4.3, 3), (drill.Rating, drill.RatingCount));
    }
}
