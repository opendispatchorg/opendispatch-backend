namespace OpenDispatch.Domain.ValueObjects;

/// <summary>
/// A point on the earth in WGS 84 decimal degrees.
/// </summary>
/// <remarks>
/// <para>
/// Validated at construction so the scheduler can treat any <see cref="GeoPoint"/> it is
/// handed as a real place. A haversine distance over a NaN or a longitude of 4,000 does not
/// fail — it quietly returns garbage that propagates through the whole objective function,
/// so the check belongs here rather than in the code that consumes it.
/// </para>
/// <para>
/// The properties are read-only, which also means a <c>with</c> expression cannot smuggle
/// an out-of-range value past the constructor; a different point is a new
/// <see cref="GeoPoint"/>.
/// </para>
/// </remarks>
public readonly record struct GeoPoint(double Lat, double Lng)
{
    private const double MinLatitude = -90d;
    private const double MaxLatitude = 90d;
    private const double MinLongitude = -180d;
    private const double MaxLongitude = 180d;

    /// <summary>Latitude in decimal degrees, between -90 and 90 inclusive.</summary>
    public double Lat { get; } = IsWithin(Lat, MinLatitude, MaxLatitude)
        ? Lat
        : throw new ArgumentOutOfRangeException(
            nameof(Lat), Lat, "Latitude must be a finite value between -90 and 90 degrees.");

    /// <summary>Longitude in decimal degrees, between -180 and 180 inclusive.</summary>
    public double Lng { get; } = IsWithin(Lng, MinLongitude, MaxLongitude)
        ? Lng
        : throw new ArgumentOutOfRangeException(
            nameof(Lng), Lng, "Longitude must be a finite value between -180 and 180 degrees.");

    // The finiteness check is not redundant: NaN compares false against every bound, so a
    // range check on its own lets it through.
    private static bool IsWithin(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
}
