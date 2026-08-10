using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.ListCustomers;

/// <summary>
/// A customer as a list shows them: enough to recognise and to reach, and nothing else.
/// </summary>
/// <param name="Id">Their identity, which is what a caller picks them by.</param>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">Their email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">Their phone number, or <see langword="null"/> if there isn't one.</param>
/// <remarks>
/// Deliberately not <c>CustomerDetail</c>. A list of full details is every address of every
/// customer on the books sent to a picker that shows a name — the one place this slice does
/// enough traffic for the difference to matter.
/// </remarks>
public sealed record CustomerSummary(CustomerId Id, string Name, string? Email, string? Phone);
