using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Sync;

/// <summary>
/// The refusals a pushed operation can meet that are not already somebody else's vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately short, because most of what a push refuses has already been said elsewhere. A
/// status operation the state machine forbids is <c>JobErrors.IllegalTransition</c> — the same
/// code and the same sentence a dispatcher gets from the REST endpoint, because a phone and a
/// board asking the same question must not get two answers. An operation naming a job this tenant
/// does not have is <c>JobErrors.NotFound</c>, for the same reason.
/// </para>
/// <para>
/// What is left is the two things only sync can produce: an operation this server cannot make
/// sense of at all, and a free-text edit that lost.
/// </para>
/// <para>
/// Every one of these rides inside a <em>successful</em> push response rather than failing it, so
/// the category never reaches the edge as a status code. <see cref="ErrorCategory.Conflict"/> is
/// still the honest label: its own definition is "retrying unchanged will fail the same way",
/// which is exactly what a device needs to know about all of them.
/// </para>
/// </remarks>
public static class SyncErrors
{
    /// <summary>The code every operation this server cannot apply at all carries.</summary>
    public const string UnsupportedOperationCode = "sync.unsupportedOperation";

    /// <summary>The code every operation whose payload cannot be read carries.</summary>
    public const string MalformedOperationCode = "sync.malformedOperation";

    /// <summary>The code a free-text edit that lost last-write-wins carries.</summary>
    public const string NotesSupersededCode = "sync.notesSuperseded";

    /// <summary>
    /// Reports an operation of a kind this server does not know how to apply.
    /// </summary>
    /// <param name="entity">What the operation said it was about.</param>
    /// <param name="type">What it said was done.</param>
    /// <remarks>
    /// Per operation rather than a refusal of the whole batch, and that is the decision worth
    /// knowing about: a device deployed ahead of its server would otherwise hold a queue that can
    /// never be emptied, because the one operation this server cannot read would take the
    /// technician's whole morning down with it every time it was retried.
    /// </remarks>
    public static Error UnsupportedOperation(string entity, string type) =>
        Error.Conflict(
            UnsupportedOperationCode,
            $"This server cannot apply a '{type}' operation on a '{entity}'.");

    /// <summary>
    /// Reports an operation whose payload does not say what that kind of operation needs, or says
    /// something the domain refuses.
    /// </summary>
    /// <param name="type">What was done.</param>
    /// <param name="because">
    /// What was wrong with it, in words fit to show the technician holding the phone.
    /// </param>
    public static Error MalformedOperation(string type, string because) =>
        Error.Conflict(MalformedOperationCode, $"That '{type}' operation could not be applied: {because}");

    /// <summary>
    /// Reports free text that lost to something written later.
    /// </summary>
    /// <param name="recordedAt">When the notes that stand were written.</param>
    /// <remarks>
    /// Named for what happened rather than for the mechanism: the device's note was not rejected
    /// for being malformed or illegal, it was simply older than the one the server holds. The
    /// device rebases by pulling with the cursor the push returned, which is where it learns what
    /// the newer note actually says.
    /// </remarks>
    public static Error NotesSuperseded(DateTimeOffset recordedAt) =>
        Error.Conflict(
            NotesSupersededCode,
            $"Notes written at {recordedAt:u} are newer than these, so these were not kept.");
}
