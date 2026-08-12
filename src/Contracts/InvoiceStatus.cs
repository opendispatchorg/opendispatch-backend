using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts;

/// <summary>
/// Whether an invoice has been settled, as the clients see it.
/// </summary>
/// <remarks>
/// A deliberate copy of the domain's <c>InvoiceStatus</c>, for the same reason <see cref="JobStatus"/>
/// is one — Contracts depends on nothing (Document 2 §2), and a test compares the two enums name
/// for name and number for number so the copy cannot quietly drift.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceStatus>))]
public enum InvoiceStatus
{
    /// <summary>Raised but not settled.</summary>
    Draft = 0,

    /// <summary>Settled. Terminal.</summary>
    Paid = 1,
}
