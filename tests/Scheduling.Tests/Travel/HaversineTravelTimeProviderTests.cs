using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Travel;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Scheduling.Tests.Travel;

/// <summary>
/// The one number the whole objective function is built out of. A travel estimate that is
/// wrong by a constant factor still schedules sensibly; one that is asymmetric, or that
/// returns NaN at some latitude nobody tested, produces schedules that are wrong in ways no
/// amount of reading the solver will explain.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class HaversineTravelTimeProviderTests
{
    private const double SpeedKph = 40d;

    private static readonly GeoPoint CharingCross = new(51.5074d, -0.1278d);
    private static readonly GeoPoint Camden = new(51.5390d, -0.1426d);
    private static readonly GeoPoint Paris = new(48.8566d, 2.3522d);
    private static readonly GeoPoint Sydney = new(-33.8688d, 151.2093d);

    private static readonly HaversineTravelTimeProvider Provider = new(SpeedKph);

    public static TheoryData<GeoPoint, GeoPoint> Pairs =>
        new()
        {
            { CharingCross, Camden },
            { CharingCross, Paris },
            { CharingCross, Sydney },
            { Sydney, Paris },
            { new GeoPoint(0d, -179.9d), new GeoPoint(0d, 179.9d) },
            { new GeoPoint(89.9d, 0d), new GeoPoint(-89.9d, 180d) },
        };

    /// <summary>
    /// Distances the estimate has to land near, in kilometres, from published great-circle
    /// figures. The tolerance is a tenth of a percent — loose enough for the choice of earth
    /// radius, tight enough that a wrong formula cannot hide.
    /// </summary>
    public static TheoryData<GeoPoint, GeoPoint, double> KnownDistances =>
        new()
        {
            // A degree of latitude at the equator, and a degree of longitude alongside it.
            { new GeoPoint(0d, 0d), new GeoPoint(1d, 0d), 111.19d },
            { new GeoPoint(0d, 0d), new GeoPoint(0d, 1d), 111.19d },

            // A degree of longitude shrinks with the cosine of the latitude: 111.19 * cos(60).
            { new GeoPoint(60d, 0d), new GeoPoint(60d, 1d), 55.60d },

            // Real pairs, the scale the scheduler actually works at and well past it.
            { CharingCross, Camden, 3.66d },
            { CharingCross, Paris, 343.6d },
            { CharingCross, Sydney, 16_993d },

            // Antipodes: half the circumference. The second pair is one where the haversine
            // term rounds to just over 1, the case an unclamped Asin turns into NaN.
            { new GeoPoint(0d, 0d), new GeoPoint(0d, 180d), 20_015d },
            { new GeoPoint(0.0074d, 0d), new GeoPoint(-0.0074d, 180d), 20_015d },
        };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheDriveBackIsTheSameLengthAsTheDriveOut(GeoPoint from, GeoPoint to)
    {
        // Exact equality, not near-equality: the implementation computes both directions from
        // identical operands on purpose, so a route and its reverse can never score
        // differently by a rounding error nobody could track down.
        Assert.Equal(Provider.Minutes(from, to), Provider.Minutes(to, from));
    }

    [Theory]
    [MemberData(nameof(KnownDistances))]
    public void EstimatesTheDistanceBetweenTwoPlaces(GeoPoint from, GeoPoint to, double expectedKm)
    {
        var km = Provider.Minutes(from, to) / 60d * SpeedKph;

        Assert.InRange(km, expectedKm * 0.999d, expectedKm * 1.001d);
    }

    [Fact]
    public void GoingNowhereTakesNoTime()
    {
        Assert.Equal(0d, Provider.Minutes(CharingCross, CharingCross));
    }

    [Fact]
    public void DrivingTwiceAsFastTakesHalfAsLong()
    {
        var slow = new HaversineTravelTimeProvider(SpeedKph).Minutes(CharingCross, Camden);
        var fast = new HaversineTravelTimeProvider(SpeedKph * 2d).Minutes(CharingCross, Camden);

        Assert.Equal(slow / 2d, fast, 9);
    }

    [Fact]
    public void ACrossTownDriveTakesAPlausibleNumberOfMinutes()
    {
        // Charing Cross to Camden is about 3.7km straight line. At the default urban average
        // that is a few minutes — the check is that the units are minutes and not hours,
        // seconds or kilometres, which is the kind of mistake that silently rescales the
        // whole objective function.
        var minutes = new HaversineTravelTimeProvider().Minutes(CharingCross, Camden);

        Assert.InRange(minutes, 3d, 8d);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-40d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RefusesASpeedThatIsNotADrivableOne(double speedKph)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HaversineTravelTimeProvider(speedKph));
    }

    [Fact]
    public void TheMatrixAgreesWithTheSingleEstimateForEveryPair()
    {
        var points = new[] { CharingCross, Camden, Paris, Sydney };

        var matrix = Provider.Matrix(points);

        for (var i = 0; i < points.Length; i++)
        {
            for (var j = 0; j < points.Length; j++)
            {
                Assert.Equal(Provider.Minutes(points[i], points[j]), matrix[i][j]);
            }
        }
    }

    [Fact]
    public void TheMatrixIsSquareWithAZeroDiagonal()
    {
        var points = new[] { CharingCross, Camden, Paris };

        var matrix = Provider.Matrix(points);

        Assert.Equal(points.Length, matrix.Length);
        Assert.All(matrix, row => Assert.Equal(points.Length, row.Length));

        for (var i = 0; i < points.Length; i++)
        {
            Assert.Equal(0d, matrix[i][i]);
        }
    }

    [Fact]
    public void AnEmptyMatrixIsNotAnError()
    {
        // A day with nothing to do is a real day, and the greedy constructor will ask for its
        // matrix like any other.
        Assert.Empty(Provider.Matrix([]));
    }
}
