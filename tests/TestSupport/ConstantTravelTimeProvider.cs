using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.TestSupport;

/// <summary>
/// A travel-time provider where every drive takes the same number of minutes.
/// </summary>
/// <remarks>
/// For tests about arithmetic rather than about geography. The objective is a weighted sum of
/// four terms, and checking it against a hand-computed figure means knowing every drive
/// exactly — which a haversine over real coordinates makes tedious and a flat rate makes
/// trivial. Tests that are genuinely about distance use the real provider.
/// </remarks>
/// <param name="minutes">What every drive between two different places costs.</param>
public sealed class ConstantTravelTimeProvider(double minutes) : ITravelTimeProvider
{
    /// <summary>What every drive between two different places costs.</summary>
    public double MinutesPerDrive { get; } = minutes;

    /// <inheritdoc />
    /// <remarks>Standing still is still free, so a matrix built from this has a zero diagonal like any other.</remarks>
    public double Minutes(GeoPoint from, GeoPoint to) => from == to ? 0d : MinutesPerDrive;

    /// <inheritdoc />
    public double[][] Matrix(IReadOnlyList<GeoPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var matrix = new double[points.Count][];

        for (var i = 0; i < points.Count; i++)
        {
            matrix[i] = new double[points.Count];

            for (var j = 0; j < points.Count; j++)
            {
                matrix[i][j] = Minutes(points[i], points[j]);
            }
        }

        return matrix;
    }
}
