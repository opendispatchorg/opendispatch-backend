using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts;

/// <summary>
/// Where a job has got to in its life, as the clients see it.
/// </summary>
/// <remarks>
/// <para>
/// A deliberate copy of the domain's <c>JobStatus</c> rather than a reference to it. Contracts
/// depends on nothing (Document 2 §2), so the wire vocabulary cannot be the domain's own type
/// without dragging the domain into the package every client compiles against. The copy is
/// kept honest by a test comparing the two enums name for name and number for number, so
/// adding a status to the domain and forgetting the wire fails the build here rather than
/// surfacing as a client that has never heard of it.
/// </para>
/// <para>
/// It travels as its name — <c>"EnRoute"</c>, not <c>3</c>. A sync payload sitting in a jsonb
/// column and a board event in a browser console are both read by people, and a TypeScript
/// client matching on a string cannot quietly come to mean something else the day a number
/// moves. The numbers are still declared explicitly: they mirror the domain's, and they are
/// what a stored value means.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    /// <summary>Booked, but nobody is going to it yet.</summary>
    Unscheduled = 0,

    /// <summary>A technician and a time have been planned for it.</summary>
    Scheduled = 1,

    /// <summary>Sent to the technician's phone; it is on their day.</summary>
    Dispatched = 2,

    /// <summary>The technician is travelling to it.</summary>
    EnRoute = 3,

    /// <summary>The technician is on site doing the work.</summary>
    InProgress = 4,

    /// <summary>The work is done. Nothing further happens to the job in the field.</summary>
    Completed = 5,

    /// <summary>The completed work has been turned into an invoice.</summary>
    Invoiced = 6,

    /// <summary>The invoice has been settled. Terminal.</summary>
    Paid = 7,

    /// <summary>Called off before the work was finished. Terminal.</summary>
    Cancelled = 8,
}
