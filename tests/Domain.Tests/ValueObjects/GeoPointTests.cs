using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests.ValueObjects;

/// <summary>
/// The point of validating here is that the scheduler never has to: a haversine distance
/// over a NaN or an impossible longitude does not fail, it returns garbage that spreads
/// through the objective function.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class GeoPointTests
{
    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(90d, 180d)]
    [InlineData(-90d, -180d)]
    [InlineData(51.5074d, -0.1278d)]
    public void AcceptsCoordinatesWithinRangeIncludingTheBounds(double lat, double lng)
    {
        var point = new GeoPoint(lat, lng);

        Assert.Equal(lat, point.Lat);
        Assert.Equal(lng, point.Lng);
    }

    [Theory]
    [InlineData(90.0001d)]
    [InlineData(-90.0001d)]
    [InlineData(180d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsAnImpossibleLatitude(double lat)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(lat, 0d));
    }

    [Theory]
    [InlineData(180.0001d)]
    [InlineData(-180.0001d)]
    [InlineData(360d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsAnImpossibleLongitude(double lng)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoPoint(0d, lng));
    }
}
