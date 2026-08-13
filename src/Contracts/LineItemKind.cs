using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts;

/// <summary>
/// What a billed line is for, as the clients see it.
/// </summary>
/// <remarks>
/// A deliberate copy of the domain's <c>LineItemKind</c>, for the same reason <see cref="JobStatus"/>
/// is one — Contracts depends on nothing (Document 2 §2), and a test compares the two enums name
/// for name and number for number so the copy cannot quietly drift.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LineItemKind>))]
public enum LineItemKind
{
    /// <summary>Time on the job, billed by the hour.</summary>
    Labor = 0,

    /// <summary>Something fitted or supplied, billed by the unit.</summary>
    Part = 1,
}
