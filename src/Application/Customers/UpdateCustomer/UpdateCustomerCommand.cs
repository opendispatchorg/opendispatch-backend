using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.UpdateCustomer;

/// <summary>Corrects a customer's name and contact details.</summary>
/// <param name="Id">Which customer.</param>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">An email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">A phone number, or <see langword="null"/> if there isn't one.</param>
/// <remarks>
/// States the whole record rather than a change to it, the same shape
/// <c>CreateCustomerCommand</c> already takes — a form with three fields submits three fields,
/// whichever of them actually changed.
/// </remarks>
public sealed record UpdateCustomerCommand(CustomerId Id, string Name, string? Email, string? Phone) : ICommand;
