using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Notifications;

/// <summary>
/// Where a deployment's mail goes, and what it signs itself as.
/// </summary>
/// <param name="Host">The SMTP server. This is what decides whether anything is sent at all.</param>
/// <param name="Port">Its port.</param>
/// <param name="From">The address messages come from.</param>
/// <param name="FromName">The display name beside it, or null for the address alone.</param>
/// <param name="Username">The login, or null for a relay that wants none.</param>
/// <param name="Password">Its password.</param>
/// <remarks>
/// The password is a configuration value, unlike the object store's credentials, which have no key
/// at all. The difference is not a change of mind: an S3 client resolves keys from the environment
/// by itself, so there was somewhere better to put them; SMTP has no such convention, so the value
/// has to arrive through configuration — and a deployment supplies it as an environment variable
/// (<c>Mail__Password</c>) rather than in a committed file.
/// </remarks>
public sealed record MailSettings(
    string Host,
    int Port,
    string From,
    string? FromName,
    string? Username,
    string? Password);

/// <summary>
/// Registration for outbound notifications.
/// </summary>
public static class NotificationRegistration
{
    /// <summary>
    /// Registers the SMTP sender, if this deployment named a mail server.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configured">
    /// Whether this deployment named a mail server. Decided now rather than lazily, because whether
    /// a process can send mail at all is structural — the same reason the SignalR backplane is
    /// chosen at registration.
    /// </param>
    /// <param name="settings">
    /// Reads the mail settings once the container is built, so the values come from the bound,
    /// startup-validated options rather than from a second raw read of configuration.
    /// </param>
    /// <returns>Whether anything will be sent — the host says so at startup either way.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Nothing is registered when nothing is configured</strong>, and that is the design
    /// rather than an oversight. <c>INotificationSender</c> was deliberately left unimplemented
    /// through the whole build rather than given a no-op, because a port with a do-nothing
    /// implementation reports success for messages nobody sent. An absent registration makes the
    /// subscriber's own "no sender" branch the truth, and the startup log says which it is — the
    /// same pattern as the SignalR backplane and the OTLP exporter.
    /// </para>
    /// <para>
    /// A singleton: it holds settings and opens a connection per message, so there is no shared
    /// state and nothing per request.
    /// </para>
    /// </remarks>
    public static bool AddNotifications(
        this IServiceCollection services,
        bool configured,
        Func<IServiceProvider, MailSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        if (!configured)
        {
            return false;
        }

        services.AddSingleton<INotificationSender>(provider => new SmtpNotificationSender(
            settings(provider),
            provider.GetRequiredService<ILogger<SmtpNotificationSender>>()));

        return true;
    }
}
