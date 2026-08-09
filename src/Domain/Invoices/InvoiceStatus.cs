namespace OpenDispatch.Domain.Invoices;

/// <summary>
/// Whether an invoice has been settled.
/// </summary>
/// <remarks>
/// Two states, deliberately. Real payment processing is out of scope for this version —
/// the loop ends at "mark paid" — so there is no Sent, Overdue or PartiallyPaid to model
/// yet. Values are numbered explicitly because this enum is mirrored to the clients.
/// </remarks>
public enum InvoiceStatus
{
    /// <summary>Raised but not settled. Still editable.</summary>
    Draft = 0,

    /// <summary>Settled. Terminal, and no longer editable.</summary>
    Paid = 1,
}
