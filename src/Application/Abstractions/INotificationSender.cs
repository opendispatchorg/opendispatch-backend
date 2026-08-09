namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Sends a message to a person outside the system.
/// </summary>
/// <remarks>
/// <para>
/// The other roadmap seam (Document 2 §13): appointment reminders and "your technician is on
/// the way" are the next thing after payments, and they arrive as handlers for domain events
/// that already exist — <c>JobDispatched</c>, <c>JobEnRoute</c> — calling this. Nothing that
/// raises those events has to change, which is the extension model working as intended.
/// </para>
/// <para>
/// Nothing implements it and nothing calls it in v1. It is declared here so that the
/// handlers, when they come, are additions rather than a port plus its callers plus an
/// adapter all landing at once.
/// </para>
/// </remarks>
public interface INotificationSender
{
    /// <summary>
    /// Sends one message, on whichever channel it names.
    /// </summary>
    /// <remarks>
    /// Returns nothing because there is nothing useful to say: delivery is asynchronous at
    /// the carrier, so a message accepted here can still fail an hour later, and a caller
    /// that treated the absence of an exception as proof of arrival would be wrong. Failing
    /// to <em>hand over</em> a message throws — that is a broken integration, not an
    /// expected outcome.
    /// </remarks>
    Task SendAsync(Notification notification, CancellationToken ct);
}
