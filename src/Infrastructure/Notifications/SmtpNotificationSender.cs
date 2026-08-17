using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Notifications;

/// <summary>
/// Sends notifications over SMTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>SMTP because it is the lowest common denominator.</strong> Resend, SES, Postmark,
/// SendGrid and a shop's own mailbox all speak it, so one adapter serves every provider a
/// deployment is likely to have without this repository picking a vendor — the same argument the
/// object-store adapter makes for S3. A provider's own HTTP API buys deliverability reporting and
/// costs a coupling; that trade is worth making when somebody has a provider, not before.
/// </para>
/// <para>
/// <strong>Only email.</strong> <see cref="NotificationChannel.Sms"/> is in the port because the
/// roadmap names it, and nothing in this system sends one; an SMTP client that quietly dropped a
/// text message would be worse than one that says it cannot send it. A carrier adapter is a class
/// beside this one, registered the same way, the day a deployment has a carrier.
/// </para>
/// <para>
/// A connection per message rather than a pooled client. The volume here is a handful of messages
/// per job, an <c>SmtpClient</c> is not thread-safe, and a long-lived connection to a mail server
/// is a thing that goes stale in ways that are discovered at the worst moment. Connecting costs a
/// round trip on a path that is already asynchronous and already off the request's critical answer.
/// </para>
/// </remarks>
internal sealed class SmtpNotificationSender(MailSettings settings, ILogger<SmtpNotificationSender> log)
    : INotificationSender
{
    public async Task SendAsync(Notification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Channel is not NotificationChannel.Email)
        {
            throw new NotSupportedException(
                $"This deployment has no {notification.Channel} sender configured; only email is sent over SMTP.");
        }

        var message = new MimeMessage
        {
            // Never null on this path: Notification.Email requires one, and the channel check above
            // is what rules out the SMS shape that has none.
            Subject = notification.Subject ?? string.Empty,
            Body = new TextPart(TextFormat.Plain) { Text = notification.Body },
        };

        message.From.Add(new MailboxAddress(settings.FromName ?? settings.From, settings.From));
        message.To.Add(MailboxAddress.Parse(notification.To));

        using var client = new SmtpClient();

        // Auto: STARTTLS where the server offers it, implicit TLS on 465, and plain only where
        // there is no other option — which is what a MailHog or a Mailpit in a compose file is.
        // Named rather than defaulted so the intent is on the page.
        await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.Auto, ct).ConfigureAwait(false);

        // A relay on a private network often wants no credentials at all, and offering them to one
        // that does not advertise AUTH is an error rather than a courtesy.
        if (settings.Username is { Length: > 0 } username)
        {
            await client.AuthenticateAsync(username, settings.Password ?? string.Empty, ct).ConfigureAwait(false);
        }

        await client.SendAsync(message, ct).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, ct).ConfigureAwait(false);

        MailLog.Sent(log, notification.Subject);
    }
}

/// <summary>What the adapter has to say, as a source-generated log method.</summary>
internal static partial class MailLog
{
    /// <remarks>
    /// The recipient is not logged. An address is personal data, and this line exists to answer
    /// "did the message leave" — the correlation id on the surrounding request is what ties it to
    /// the job it was about.
    /// </remarks>
    [LoggerMessage(Level = LogLevel.Information, Message = "Sent a notification ({Subject}).")]
    internal static partial void Sent(ILogger logger, string? subject);
}
