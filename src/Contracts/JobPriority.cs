using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts;

/// <summary>
/// How badly a job needs doing, as the clients see it.
/// </summary>
/// <remarks>
/// A deliberate copy of the domain's <c>JobPriority</c>, for the same reason <see cref="JobStatus"/>
/// is one — Contracts depends on nothing (Document 2 §2), and a test compares the two enums name
/// for name and number for number so the copy cannot quietly drift.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobPriority>))]
public enum JobPriority
{
    /// <summary>Can wait. Slipping it to another day costs little.</summary>
    Low = 1,

    /// <summary>Ordinary booked work.</summary>
    Normal = 2,

    /// <summary>Wants doing today.</summary>
    High = 3,

    /// <summary>No heat, no water, no power. Bump whatever it takes.</summary>
    Emergency = 4,
}
