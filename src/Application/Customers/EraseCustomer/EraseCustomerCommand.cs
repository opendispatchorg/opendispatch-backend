using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.EraseCustomer;

/// <summary>
/// Erases a customer's personal data at their request, keeping the financial record.
/// </summary>
/// <param name="Id">Whose data to erase.</param>
/// <remarks>
/// <para>
/// The command a shop runs when somebody asks to be forgotten. It reaches further than any other
/// write in this system — the customer, their locations, every job they were ever booked for and
/// every photograph taken on those visits — because personal data is not tidily in one aggregate:
/// a job keeps its own copy of the coordinates it was booked at, and a photograph of a boiler
/// cupboard is a picture of somebody's home.
/// </para>
/// <para>
/// <strong>It is not a delete.</strong> What survives is what a shop is required to keep and what a
/// customer's request cannot reach: the jobs, their dates and statuses, the hours and parts on them,
/// and the invoices raised from them, still totalling what they totalled.
/// </para>
/// <para>
/// One command rather than an admin verb, deliberately: it is answering a legal request about
/// production data, and the audit trail should be able to say who did it and when — which it can,
/// because this goes through the same pipeline as every other write.
/// </para>
/// </remarks>
public sealed record EraseCustomerCommand(CustomerId Id) : ICommand;
