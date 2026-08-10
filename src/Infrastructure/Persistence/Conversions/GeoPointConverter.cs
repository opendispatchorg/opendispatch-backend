using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/// <summary>
/// Stores <see cref="GeoPoint"/> as a PostGIS <c>geography(Point, 4326)</c> through
/// NetTopologySuite.
/// </summary>
/// <remarks>
/// <para>
/// A pair of <c>double precision</c> columns would round-trip just as well and be useless for
/// anything else: PostGIS distance, containment and the GiST index step 27 adds all need a real
/// geography column. The domain keeps its own dependency-free <see cref="GeoPoint"/> and never
/// sees NetTopologySuite — the type is a persistence concern, and this is the one place the two
/// meet.
/// </para>
/// <para>
/// The axis order flips here, and that is the whole reason this class is worth reading:
/// <see cref="GeoPoint"/> is (latitude, longitude) as people say it, while a
/// <see cref="Point"/> is (X, Y) — longitude first. SRID 4326 is set on the way out because a
/// geometry without one cannot be compared to anything.
/// </para>
/// </remarks>
internal sealed class GeoPointConverter()
    : ValueConverter<GeoPoint, Point>(
        point => new Point(point.Lng, point.Lat) { SRID = Wgs84 },
        point => new GeoPoint(point.Y, point.X))
{
    /// <summary>WGS 84 — the spatial reference the domain's decimal degrees are already in.</summary>
    internal const int Wgs84 = 4326;

    /// <summary>
    /// The column type every <see cref="GeoPoint"/> is stored in. Named here rather than at the
    /// registration so the type and the conversion that produces it cannot disagree.
    /// </summary>
    internal const string ColumnType = "geography (Point,4326)";
}
