using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers;

/// <summary>
/// The expected failures this slice can report.
/// </summary>
/// <remarks>
/// <para>
/// A code is what a client branches on, so it belongs to the contract in the way a message does
/// not — and a code written out at each of the two places that report it is a code that will
/// eventually be written out differently at one of them.
/// </para>
/// <para>
/// "This tenant has no such customer" covers both a customer that never existed and one that
/// belongs to somebody else: the query filters mean this tenant genuinely cannot see it, and an
/// answer that distinguished the two would confirm the existence of another tenant's row.
/// </para>
/// </remarks>
public static class CustomerErrors
{
    /// <summary>The code every "no such customer" failure carries.</summary>
    public const string NotFoundCode = "customer.notFound";

    /// <summary>The code every "that customer has no such location" failure carries.</summary>
    public const string LocationNotFoundCode = "customer.locationNotFound";

    /// <summary>The code every "that customer has been erased" failure carries.</summary>
    public const string ErasedCode = "customer.erased";

    /// <summary>Names a customer this tenant does not have.</summary>
    /// <param name="id">The customer that was asked for.</param>
    public static Error NotFound(CustomerId id) =>
        Error.NotFound(NotFoundCode, $"There is no customer {id.Value}.");

    /// <summary>
    /// Says that the customer is still on the books but has been erased, so nothing may be written
    /// about them.
    /// </summary>
    /// <remarks>
    /// A conflict rather than a not-found: the record exists and the caller is not mistaken about
    /// that — what has changed is that it may no longer be added to. The aggregate refuses this
    /// anyway; this is what turns the refusal into an answer instead of a 500, and gives a client
    /// something to branch on other than English.
    /// </remarks>
    /// <param name="id">The customer that was asked about.</param>
    public static Error Erased(CustomerId id) =>
        Error.Conflict(ErasedCode, $"Customer {id.Value} has been erased and cannot be changed.");

    /// <summary>
    /// Names a service location the customer does not have.
    /// </summary>
    /// <remarks>
    /// A separate code from <see cref="NotFound(CustomerId)"/> because the two mean different
    /// things to whoever is asking: the customer exists and the site does not, which is a stale
    /// picker rather than a stale customer. It lives here rather than in the slice that reports it
    /// — a job pointing at a location that is not on the customer's books is the only place this
    /// arises — because the vocabulary is the customer's.
    /// </remarks>
    /// <param name="customerId">The customer whose books were checked.</param>
    /// <param name="locationId">The service location that was asked for.</param>
    public static Error LocationNotFound(CustomerId customerId, ServiceLocationId locationId) =>
        Error.NotFound(
            LocationNotFoundCode,
            $"Customer {customerId.Value} has no service location {locationId.Value}.");
}
