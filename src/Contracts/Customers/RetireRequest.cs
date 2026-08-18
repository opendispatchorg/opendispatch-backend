namespace OpenDispatch.Contracts.Customers;

/// <summary>
/// The body of <c>POST /customers/{id}/retire</c> and <c>POST /technicians/{id}/retire</c>.
/// </summary>
/// <param name="Retired">
/// <see langword="true"/> to take them off the books, <see langword="false"/> to put them back.
/// </param>
/// <remarks>
/// One shape and one route per direction rather than a <c>retire</c> and an <c>unretire</c>
/// endpoint: it is one decision with two directions, and a mistake either way is undone by sending
/// the other. Shared between customers and technicians because the question is identical — naming
/// it <c>RetireRequest</c> rather than per-aggregate keeps the clients from carrying two records
/// with one boolean each.
/// </remarks>
public sealed record RetireRequest(bool Retired);
