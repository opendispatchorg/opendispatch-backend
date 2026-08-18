using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.RetireCustomer;

/// <summary>
/// Takes a customer off the books, or puts them back on.
/// </summary>
/// <param name="Id">Whose record.</param>
/// <param name="Retired">
/// <see langword="true"/> to retire them, <see langword="false"/> to reinstate. One command rather
/// than two because it is one decision with two directions, and a mistake in either is undone by
/// sending the other.
/// </param>
/// <remarks>
/// <para>
/// The thing every shop needs within a month and this system had no answer for: a customer who has
/// moved away, a site that closed, a duplicate created by a typo. There is no delete — their name
/// is on jobs and invoices the business is required to keep — so what "remove" has to mean is
/// <em>stop offering them</em>.
/// </para>
/// <para>
/// <strong>It is not erasure.</strong> Erasure answers a legal request, destroys what says who
/// somebody is, and cannot be undone. This hides a record from the lists people pick from and is
/// undone by sending it again with <c>Retired: false</c>. A shop reaching for the wrong one of
/// these should find that out from the words rather than from the consequences.
/// </para>
/// </remarks>
public sealed record RetireCustomerCommand(CustomerId Id, bool Retired) : ICommand;
