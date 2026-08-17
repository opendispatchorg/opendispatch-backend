using System.ComponentModel.DataAnnotations;
using OpenDispatch.Infrastructure.Notifications;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// Where this deployment's outbound email goes, bound from the <c>Mail</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Naming a <see cref="Host"/> is what turns notifications on.</strong> Leave it blank and
/// this host sends nothing, fails nothing, and says so once at startup — the same shape as the
/// SignalR backplane and the OTLP exporter. A shop that does not want automated email is not a
/// misconfigured deployment.
/// </para>
/// <para>
/// SMTP rather than a provider's API, because every provider speaks it: Resend, SES, Postmark and a
/// shop's own mailbox are all a host, a port and a login.
/// </para>
/// <para>
/// <see cref="Password"/> is a secret and belongs in an environment variable
/// (<c>Mail__Password</c>), never in a committed file — unlike the object store's credentials,
/// which have no configuration key at all because the S3 client reads the environment itself. SMTP
/// has no such convention, so the value passes through here.
/// </para>
/// </remarks>
public sealed class MailOptions : IValidatableObject
{
    public const string SectionName = "Mail";

    /// <summary>The SMTP server. Blank means this host sends no notifications.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Its port. 587 is submission with STARTTLS, which is what most providers want.</summary>
    [Range(1, 65_535)]
    public int Port { get; init; } = 587;

    /// <summary>The address messages come from. Required once a host is named.</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>The display name beside the address — the shop's name, as a customer reads it.</summary>
    public string FromName { get; init; } = string.Empty;

    /// <summary>The login, or blank for a relay that wants none.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Its password. Supply through the environment, not a file.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Whether this deployment sends notifications at all.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// The same question, asked of raw configuration before the container exists.
    /// </summary>
    /// <remarks>
    /// Registration has to decide whether to register an adapter at all, and the bound options are
    /// not built yet at that point. One key is read twice — here, and through the validated options
    /// once the container is up — which is the same arrangement <c>BoardBackplane</c> has, and is
    /// cheaper than a service locator or a second source of truth about what "configured" means.
    /// </remarks>
    public static bool IsConfiguredIn(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return !string.IsNullOrWhiteSpace(configuration[$"{SectionName}:{nameof(Host)}"]);
    }

    /// <summary>What the adapter needs, as the shape Infrastructure understands.</summary>
    public MailSettings ToMailSettings() => new(
        Host,
        Port,
        From,
        Blank(FromName),
        Blank(Username),
        Blank(Password));

    /// <summary>
    /// The rule a <c>[Required]</c> cannot state: what is needed depends on whether mail is on at
    /// all.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsConfigured)
        {
            yield break;
        }

        // Caught at startup rather than at the first job going en route, where the failure is a log
        // line on a path nobody is watching and a customer who was never told.
        if (string.IsNullOrWhiteSpace(From))
        {
            yield return new ValidationResult(
                "Mail:From is required when Mail:Host is configured — a message needs a sender.",
                [nameof(From)]);
        }
        else if (!From.Contains('@', StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"Mail:From is not an email address: '{From}'.",
                [nameof(From)]);
        }

        // A username with no password is the mistake that produces an authentication failure per
        // message rather than a refusal to start.
        if (!string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(Password))
        {
            yield return new ValidationResult(
                "Mail:Password is required when Mail:Username is set.",
                [nameof(Password)]);
        }
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Registration for <see cref="MailOptions"/>.
/// </summary>
public static class MailOptionsRegistration
{
    /// <summary>
    /// Binds and validates <see cref="MailOptions"/>. Validation runs at startup, so a host with
    /// half a mail configuration refuses to serve rather than discovering it one customer at a time.
    /// </summary>
    public static IServiceCollection AddMailOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MailOptions>()
            .Bind(configuration.GetSection(MailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
