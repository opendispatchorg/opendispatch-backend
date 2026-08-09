namespace OpenDispatch.Contracts.Board;

/// <summary>
/// A technician is somewhere new — the pin on the board's map. Pushed to the org's board
/// group under <see cref="BoardEvents.TechnicianMoved"/>.
/// </summary>
/// <remarks>
/// <para>
/// The third of Document 2 §9's three board events, and the only one that is not an aggregate
/// announcing itself: nothing in the domain records where a technician currently is, because
/// a live position is not a business fact anybody needs to keep — it is a pin that is stale
/// within the minute. It is defined here because the board's map is specified and the shape
/// the clients read has to exist before anything can publish it.
/// </para>
/// <para>
/// Latitude and longitude are flat rather than a nested point so a client can read
/// <c>lat</c>/<c>lng</c> straight into whichever map library it uses. Contracts has no
/// <c>GeoPoint</c> of its own for the same reason it has no domain reference at all, and a
/// wire type invented to wrap two doubles would be a type each client then has to unwrap.
/// </para>
/// </remarks>
/// <param name="TechnicianId">Whose pin moves.</param>
/// <param name="Lat">Latitude in degrees.</param>
/// <param name="Lng">Longitude in degrees.</param>
/// <param name="At">
/// When the device was there, not when the message was sent. A phone that was out of signal
/// reports a position several minutes old, and a board that showed it as current would be
/// confidently wrong.
/// </param>
public sealed record TechnicianMoved(Guid TechnicianId, double Lat, double Lng, DateTimeOffset At);
