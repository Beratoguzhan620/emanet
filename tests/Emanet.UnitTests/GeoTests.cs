using Emanet.Core;
using Emanet.Core.Geo;

namespace Emanet.UnitTests;

public sealed class GeoTests
{
    [Fact]
    public void Points_are_longitude_first_like_PostGIS_and_rounded_to_a_tenth_of_a_metre()
    {
        var point = GeoPoint.At(41.28671234, 36.33004567);

        Assert.Equal((36.330046, 41.286712, 4326), (point.X, point.Y, point.SRID));
    }

    [Theory]
    [InlineData(91, 36)]
    [InlineData(41, 181)]
    [InlineData(double.NaN, 36)]
    public void Points_must_be_on_the_earth(double lat, double lng) =>
        Assert.Throws<DomainException>(() => GeoPoint.At(lat, lng));

    [Fact]
    public void Distances_are_great_circle_kilometres()
    {
        var centre = GeoPoint.At(41.2867, 36.33);

        Assert.InRange(GeoPoint.DistanceKm(centre, GeoPoint.At(41.3335, 36.2714)), 7.0, 7.3);
        Assert.InRange(GeoPoint.DistanceKm(centre, GeoPoint.At(39.9334, 32.8597)), 325, 335);
    }

    [Theory]
    [InlineData(41.333512, 36.271438, 41.3325, 36.2725)]
    [InlineData(41.330001, 36.274999, 41.3325, 36.2725)]
    [InlineData(41.335001, 36.270001, 41.3375, 36.2725)]
    [InlineData(-33.868820, 151.209290, -33.8675, 151.2075)]
    public void Public_places_are_the_centre_of_the_cell_the_point_falls_in(double lat, double lng, double cellLat, double cellLng) =>
        Assert.Equal((cellLat, cellLng), GeoPoint.Approximate(GeoPoint.At(lat, lng)));

    [Fact]
    public void A_public_place_is_never_more_than_half_a_cell_from_the_real_one()
    {
        var random = new Random(7);
        for (var i = 0; i < 1000; i++)
        {
            var point = GeoPoint.At(36 + (random.NextDouble() * 6), 26 + (random.NextDouble() * 18));
            var (lat, lng) = GeoPoint.Approximate(point);
            Assert.InRange(Math.Abs(lat - point.Y), 0, (GeoPoint.ApproximateStep / 2) + 0.0001);
            Assert.InRange(Math.Abs(lng - point.X), 0, (GeoPoint.ApproximateStep / 2) + 0.0001);
        }
    }
}
