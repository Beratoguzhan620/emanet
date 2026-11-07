using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Emanet.Core.Geo;

/// <summary>Points on the earth (WGS 84) and the distances between them.</summary>
public static class GeoPoint
{
    public const int Srid = 4326;

    /// <summary>The grid public locations are snapped to: about 550 m north to south, a few hundred metres east to west.</summary>
    public const double ApproximateStep = 0.005;

    public static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(Srid);

    public static Point At(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A location needs a latitude from -90 to 90 and a longitude from -180 to 180.");
        }

        // X is longitude and Y latitude, as PostGIS expects.
        return Factory.CreatePoint(new Coordinate(Math.Round(longitude, 6), Math.Round(latitude, 6)));
    }

    public static double Latitude(this Point point) => point.Y;

    public static double Longitude(this Point point) => point.X;

    /// <summary>Great-circle distance in kilometres; PostGIS gives the same on a sphere.</summary>
    public static double DistanceKm(Point a, Point b)
    {
        const double EarthRadiusKm = 6371.0088;
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Y - a.Y);
        var dLon = Rad(b.X - a.X);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + (Math.Cos(Rad(a.Y)) * Math.Cos(Rad(b.Y)) * Math.Pow(Math.Sin(dLon / 2), 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>
    /// Where an item roughly is, for people who have not rented it: the centre of the grid cell the point falls in.
    /// The same point always lands in the same cell, so looking twice reveals nothing more.
    /// </summary>
    public static (double Latitude, double Longitude) Approximate(Point point)
    {
        static double Snap(double value) => Math.Round((Math.Floor(value / ApproximateStep) * ApproximateStep) + (ApproximateStep / 2), 4);
        return (Snap(point.Y), Snap(point.X));
    }
}
