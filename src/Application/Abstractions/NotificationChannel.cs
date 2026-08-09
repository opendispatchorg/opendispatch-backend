namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// How a notification reaches somebody.
/// </summary>
/// <remarks>
/// The two the roadmap names, and no more. A push notification is not here because the
/// technician app is a web app that syncs over HTTP, and a channel nobody sends on is a
/// branch nobody tests.
/// </remarks>
public enum NotificationChannel
{
    /// <summary>A text message.</summary>
    Sms = 0,

    /// <summary>An email.</summary>
    Email = 1,
}
