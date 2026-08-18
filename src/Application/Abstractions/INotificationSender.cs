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
/// <c>SmtpNotificationSender</c> implements it and <c>CustomerNotifications</c> calls it, for the
/// two moments a customer wants to hear about: their technician is on the way, and their invoice
/// has been settled. Declaring the port first turned out to be worth it — the feature arrived as
/// one subscriber and one adapter rather than as a port plus its callers plus an adapter all at
/// once.
/// </para>
/// <para>
/// <strong>It may be unregistered, and callers must cope.</strong> A deployment that names no mail
/// server gets no implementation at all rather than a no-op — a do-nothing adapter would report
/// success for messages nobody sent. <c>CustomerNotifications</c> therefore takes it as an optional
/// constructor argument and does nothing when it is absent.
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
