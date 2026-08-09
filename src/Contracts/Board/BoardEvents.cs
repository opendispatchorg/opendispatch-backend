namespace OpenDispatch.Contracts.Board;

/// <summary>
/// The names the board's messages arrive under. A payload nobody can subscribe to is half a
/// contract, so the names live beside the shapes rather than as a literal in the hub and a
/// second, hopefully identical, literal in each client.
/// </summary>
/// <remarks>
/// The names are Document 2 §9's, verbatim. They are lowercase and dotted because that is
/// what the document says and what a JavaScript client reads naturally in
/// <c>connection.on("job.updated", ...)</c>; the C# type names beside them are the payloads.
/// </remarks>
public static class BoardEvents
{
    /// <summary>Carries a <c>JobUpdated</c> payload.</summary>
    public const string JobUpdated = "job.updated";

    /// <summary>Carries an <c>AssignmentUpdated</c> payload.</summary>
    public const string AssignmentUpdated = "assignment.updated";

    /// <summary>Carries a <c>TechnicianMoved</c> payload.</summary>
    public const string TechnicianMoved = "technician.moved";
}
