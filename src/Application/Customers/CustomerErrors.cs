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

    /// <summary>Names a customer this tenant does not have.</summary>
    /// <param name="id">The customer that was asked for.</param>
    public static Error NotFound(CustomerId id) =>
        Error.NotFound(NotFoundCode, $"There is no customer {id.Value}.");
}
