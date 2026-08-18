using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians;

/// <summary>
/// The expected failures this slice can report.
/// </summary>
/// <remarks>
/// As with customers, "this tenant has no such technician" covers both one that never existed and
/// one on another organization's books: the query filters mean this tenant genuinely cannot see
/// them, and an answer that told the two apart would confirm somebody else's row exists.
/// </remarks>
public static class TechnicianErrors
{
    /// <summary>The code every "no such technician" failure carries.</summary>
    public const string NotFoundCode = "technician.notFound";

    /// <summary>Names a technician this tenant does not have.</summary>
    /// <param name="id">The technician that was asked for.</param>
    public static Error NotFound(TechnicianId id) =>
        Error.NotFound(NotFoundCode, $"There is no technician {id.Value}.");

    /// <summary>The code every attempt to plan work onto a retired technician carries.</summary>
    public const string RetiredCode = "technician.retired";

    /// <summary>
    /// Reports a technician who has left the crew.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A conflict rather than a not-found: they exist, and every stop they ever drove still points
    /// at them — what has changed is that they take no new work. Undone by reinstating them, which
    /// the message says, because the caller's next move is a decision rather than an apology.
    /// </para>
    /// <para>
    /// The optimiser already skips them and the office's list already hides them; this closes the
    /// third way in. A dispatcher dragging a stop from a stale board, or a client holding an id
    /// from before, must not be able to plan somebody's day for them after they have gone. Work
    /// they were <em>already</em> holding is untouched — retiring deliberately does not unpick a
    /// day, and this refuses only the creation of new work.
    /// </para>
    /// </remarks>
    /// <param name="id">The technician that was asked about.</param>
    public static Error Retired(TechnicianId id) =>
        Error.Conflict(
            RetiredCode,
            $"Technician {id.Value} has been retired, so no new work can be planned for them. "
            + "Reinstate them first if they are back on the crew.");
}
