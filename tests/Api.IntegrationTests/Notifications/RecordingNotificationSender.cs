using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Api.IntegrationTests.Notifications;

/// <summary>
/// A mail server that keeps what it was handed, and one that refuses everything.
/// </summary>
/// <remarks>
/// The port rather than the SMTP adapter, because what these tests are about is <em>who gets told
/// what, and when</em> — the rules a shop would be embarrassed by if they were wrong. Whether
/// MailKit can talk to a mail server is MailKit's business and not a claim this suite can settle
/// without a mail server.
/// </remarks>
internal sealed class RecordingNotificationSender : INotificationSender
{
    private readonly List<Notification> _sent = [];

    /// <summary>Whether every send throws, standing in for a mail server that is down.</summary>
    public bool Broken { get; init; }

    /// <summary>What was handed over, in order.</summary>
    public IReadOnlyList<Notification> Sent => _sent;

    public Task SendAsync(Notification notification, CancellationToken ct)
    {
        if (Broken)
        {
            throw new InvalidOperationException("the mail server is not answering");
        }

        _sent.Add(notification);

        return Task.CompletedTask;
    }
}
