using System.ComponentModel.DataAnnotations;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// Where attachment content is stored, bound from the <c>Attachments</c> configuration section.
/// </summary>
/// <remarks>
/// A relative path resolves against the host's working directory, which is convenient in
/// development and wrong in production: a deployment should give an absolute path on a volume that
/// survives the container, because these are the only bytes in the system that are not in Postgres
/// and therefore not in the database backup.
/// </remarks>
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    [Required(AllowEmptyStrings = false)]
    public string Root { get; init; } = string.Empty;
}

/// <summary>
/// Registration for <see cref="AttachmentOptions"/>.
/// </summary>
public static class AttachmentOptionsRegistration
{
    /// <summary>
    /// Binds and validates <see cref="AttachmentOptions"/>. Validation runs at startup, so a host
    /// with nowhere to put photographs fails before a technician finds out.
    /// </summary>
    public static IServiceCollection AddAttachmentOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AttachmentOptions>()
            .Bind(configuration.GetSection(AttachmentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
