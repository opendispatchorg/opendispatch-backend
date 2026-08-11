using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.CreateCustomer;

/// <summary>
/// Takes on a new customer, with no service locations yet.
/// </summary>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">An email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">A phone number, or <see langword="null"/> if there isn't one.</param>
/// <remarks>
/// <para>
/// There is no organization on it, and that is the rule this slice establishes for every slice
/// after it: <strong>a handler takes the tenant from <c>ITenantContext</c> and never from a
/// command, a DTO or a route parameter.</strong> The global query filters make cross-tenant
/// <em>reads</em> impossible, but they constrain reads only — a command that could name an
/// organization would be a way to file a row under somebody else's, and the filters would then
/// loyally hide it from the tenant that created it.
/// </para>
/// <para>
/// Contact details arrive as two strings rather than as a <c>ContactInfo</c> so the validator can
/// speak about <c>Email</c> and <c>Phone</c> by name, which is what lets a rejection say which
/// field was wrong.
/// </para>
/// </remarks>
public sealed record CreateCustomerCommand(string Name, string? Email, string? Phone)
    : ICommand<CustomerId>;
