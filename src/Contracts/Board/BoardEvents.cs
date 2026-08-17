namespace OpenDispatch.Contracts.Board;

/// <summary>
/// The names the board's messages arrive under. A payload nobody can subscribe to is half a
/// contract, so the names live beside the shapes rather than as a literal in the hub and a
/// second, hopefully identical, literal in each client.
/// </summary>
/// <remarks>
/// <para>
/// The names are Document 2 §9's. They are lowercase and dotted because that is what the document
/// says and what a JavaScript client reads naturally in <c>connection.on("job.updated", ...)</c>;
/// the C# type names beside them are the payloads.
/// </para>
/// <para>
/// <strong>Two of the document's three, not all three.</strong> Document 2 §9 also names
/// <c>technician.moved</c>, and it was published here — a name, a payload shape, and a generated
/// TypeScript interface — for a message nothing in this system has ever sent, because nothing
/// models a technician's live position and no part of the roadmap adds one. A contract that
/// promises a message the server cannot send is a contract a client writes a handler against and
/// waits forever on. It is removed rather than left as an invitation; the day a real location
/// source exists, the name and the shape are three lines and a generated file.
/// </para>
/// </remarks>
public static class BoardEvents
{
    /// <summary>Carries a <c>JobUpdated</c> payload.</summary>
    public const string JobUpdated = "job.updated";

    /// <summary>
    /// Carries an <c>AssignmentUpdated</c> payload — a stop newly planned, or one that moved.
    /// </summary>
    /// <remarks>
    /// Both, under one name deliberately: a board renders where a stop is, and has no separate
    /// way to draw an arrival. The domain keeps them apart (<c>AssignmentPlanned</c> and
    /// <c>AssignmentChanged</c>) for subscribers that need the difference; the wire does not,
    /// because no client does.
    /// </remarks>
    public const string AssignmentUpdated = "assignment.updated";
}
