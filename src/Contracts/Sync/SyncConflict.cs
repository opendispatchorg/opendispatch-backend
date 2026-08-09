namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// An operation the server would not apply, and why.
/// </summary>
/// <remarks>
/// A conflict is an ordinary outcome of working offline, not an error: a technician standing
/// in a crawlspace cannot know that the office cancelled the job an hour ago. It is reported
/// per operation so the rest of the batch still lands, and it carries words because a
/// technician has to be told something more useful than that their tap did not take.
/// </remarks>
/// <param name="OpId">Which operation, by the id the device gave it.</param>
/// <param name="Reason">What kind of refusal it was, for the client to branch on.</param>
/// <param name="Message">Why, in terms fit to show the technician holding the phone.</param>
public sealed record SyncConflict(Guid OpId, SyncConflictReason Reason, string Message);
