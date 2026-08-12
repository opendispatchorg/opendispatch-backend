using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts;

/// <summary>
/// What a technician captured, as the clients see it.
/// </summary>
/// <remarks>
/// A deliberate copy of the domain's <c>AttachmentKind</c>, for the same reason <see cref="JobStatus"/>
/// is one — Contracts depends on nothing (Document 2 §2), and a test compares the two enums name
/// for name and number for number so the copy cannot quietly drift.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AttachmentKind>))]
public enum AttachmentKind
{
    /// <summary>A picture of the site, the fault, or the finished work.</summary>
    Photo = 0,

    /// <summary>The customer's signature, captured on the technician's screen.</summary>
    Signature = 1,
}
