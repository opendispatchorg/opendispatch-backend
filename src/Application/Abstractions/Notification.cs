namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// A message bound for a customer or a technician.
/// </summary>
/// <remarks>
/// Built only through <see cref="Sms"/> and <see cref="Email"/>, because a text message with
/// a subject line is not a thing, and a nullable field plus a comment saying "email only" is
/// how it becomes one.
/// </remarks>
public sealed record Notification
{
    private Notification(NotificationChannel channel, string to, string? subject, string body)
    {
        Channel = channel;
        To = to;
        Subject = subject;
        Body = body;
    }

    /// <summary>How it is being sent.</summary>
    public NotificationChannel Channel { get; }

    /// <summary>Where to: a phone number or an email address, depending on the channel.</summary>
    /// <remarks>
    /// Unvalidated here on purpose. What counts as a reachable number varies by country and
    /// by carrier, the adapter is the only thing that knows, and rejecting a number this
    /// system merely does not recognise would lose a message that would have arrived.
    /// </remarks>
    public string To { get; }

    /// <summary>The subject line; <see langword="null"/> for anything that is not an email.</summary>
    public string? Subject { get; }

    /// <summary>What it says.</summary>
    public string Body { get; }

    /// <summary>A text message.</summary>
    public static Notification Sms(string to, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new Notification(NotificationChannel.Sms, to, null, body);
    }

    /// <summary>An email.</summary>
    public static Notification Email(string to, string subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new Notification(NotificationChannel.Email, to, subject, body);
    }
}
