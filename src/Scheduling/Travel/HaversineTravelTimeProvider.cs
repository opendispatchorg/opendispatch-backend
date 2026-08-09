using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Scheduling.Travel;

/// <summary>
/// Estimates driving time as the great-circle distance between two points at an assumed
/// average speed.
/// </summary>
/// <remarks>
/// <para>
/// The engine's default, and the reason the Scheduling project needs no infrastructure to be
/// useful or testable. It is honestly wrong in a known direction — roads are longer than
/// straight lines — so it under-estimates every drive by roughly the same factor, which
/// leaves the <em>relative</em> ordering of candidate routes largely intact. That is what the
/// search actually consumes, and it is why a straight-line estimate is a reasonable default
/// rather than a placeholder.
/// </para>
/// <para>
/// The correction is a lower <see cref="AverageSpeedKph"/>, not a detour multiplier: an
/// average of 40 km/h is not a claim about a speed limit, it is a claim about how far a van
/// gets in an hour of urban service work, and it already carries the detour, the traffic and
/// the parking.
/// </para>
/// <para>
/// Haversine rather than the spherical law of cosines because the latter loses its precision
/// exactly where service work happens — over short distances — and rather than an ellipsoidal
/// formula because sub-percent accuracy on the earth's shape is meaningless next to the
/// straight-line assumption sitting on top of it.
/// </para>
/// <para>
/// Immutable and stateless, so one instance can be shared across every solve and thread.
/// </para>
/// </remarks>
public sealed class HaversineTravelTimeProvider : ITravelTimeProvider
{
    /// <summary>
    /// Kilometres per hour a service van covers over a working day, door to door. Low on
    /// purpose: it stands in for the detour off the straight line as well as the driving.
    /// </summary>
    public const double DefaultAverageSpeedKph = 40d;

    /// <summary>
    /// The earth's mean radius in kilometres (IUGG). Any nearby value would do — the sphere
    /// is the approximation, not the radius.
    /// </summary>
    private const double EarthRadiusKm = 6371.0088d;

    private readonly double _kilometresPerMinute;

    /// <summary>Creates a provider driving at <paramref name="averageSpeedKph"/>.</summary>
    /// <param name="averageSpeedKph">
    /// Assumed average speed in kilometres per hour. Defaults to
    /// <see cref="DefaultAverageSpeedKph"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The speed is not finite and positive.</exception>
    public HaversineTravelTimeProvider(double averageSpeedKph = DefaultAverageSpeedKph)
    {
        if (!double.IsFinite(averageSpeedKph) || averageSpeedKph <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(averageSpeedKph),
                averageSpeedKph,
                "An average speed must be finite and greater than zero.");
        }

        AverageSpeedKph = averageSpeedKph;
        _kilometresPerMinute = averageSpeedKph / 60d;
    }

    /// <summary>The assumed average speed, in kilometres per hour.</summary>
    public double AverageSpeedKph { get; }

    /// <inheritdoc />
    public double Minutes(GeoPoint from, GeoPoint to) => DistanceKm(from, to) / _kilometresPerMinute;

    /// <inheritdoc />
    /// <remarks>
    /// Only the upper triangle is computed. Straight-line travel is symmetric, and computing
    /// both halves would be twice the trigonometry for an answer that has to agree with
    /// itself anyway.
    /// </remarks>
    public double[][] Matrix(IReadOnlyList<GeoPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var count = points.Count;
        var matrix = new double[count][];

        for (var i = 0; i < count; i++)
        {
            matrix[i] = new double[count];
        }

        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                var minutes = Minutes(points[i], points[j]);
                matrix[i][j] = minutes;
                matrix[j][i] = minutes;
            }
        }

        return matrix;
    }

    private static double DistanceKm(GeoPoint from, GeoPoint to)
    {
        var fromLat = double.DegreesToRadians(from.Lat);
        var toLat = double.DegreesToRadians(to.Lat);

        // Absolute differences, so the two directions are computed from identical operands
        // rather than from a sign that Math.Sin is trusted to fold away. The search compares
        // schedules by cost; a matrix that disagreed with itself by an ulp would make a route
        // and its reverse score differently for no reason anyone could ever find.
        var deltaLat = Math.Abs(toLat - fromLat);
        var deltaLng = Math.Abs(double.DegreesToRadians(to.Lng - from.Lng));

        // No need to normalise a longitude difference past 180 degrees: the half-angle sine
        // folds it back on its own, so the date line costs nothing to cross.
        var halfLat = Math.Sin(deltaLat / 2d);
        var halfLng = Math.Sin(deltaLng / 2d);
        var h = (halfLat * halfLat) + (Math.Cos(fromLat) * Math.Cos(toLat) * halfLng * halfLng);

        // Rounding pushes h an ulp over 1 for some near-antipodal pairs. The square root
        // happens to round that back to exactly 1 today, but Asin of anything above 1 is NaN,
        // and a NaN would spread silently through every cost in the search. The clamp is here
        // so the formula does not rest on how two library functions round.
        return 2d * EarthRadiusKm * Math.Asin(Math.Sqrt(Math.Min(1d, h)));
    }
}
