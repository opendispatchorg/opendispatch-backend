using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Invoicing.MarkPaid;

/// <summary>
/// Takes payment for an invoice and closes it.
/// </summary>
/// <param name="InvoiceId">The bill being settled.</param>
/// <remarks>
/// <para>
/// The end of the loop Document 1 describes as "call to cash". v1 does not process cards — the
/// gateway behind this always succeeds, which is a scoped product decision rather than a stub — but
/// the payment still goes through the port, so the day a real processor arrives it is one adapter
/// and a registration and nothing here changes.
/// </para>
/// <para>
/// No amount on the command. What is owed is the invoice's own total, computed from its lines, and
/// a caller who could name a different figure could settle a bill for the wrong money.
/// </para>
/// </remarks>
public sealed record MarkPaidCommand(InvoiceId InvoiceId) : ICommand;
