using System.ComponentModel.DataAnnotations;
using OpenDispatch.Infrastructure.Attachments;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// Where attachment content is stored, bound from the <c>Attachments</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Two ways to answer, and a deployment picks by naming a <see cref="Bucket"/> or not. Naming one
/// stores photographs and signatures in an S3-compatible object store; leaving it blank stores them
/// under <see cref="Root"/> on the host's filesystem.
/// </para>
/// <para>
/// <strong>Which one a deployment wants is not a matter of scale.</strong> A shop running this on a
/// machine it owns, with a volume it backs up, wants the disk and needs nothing else. A deployment
/// on a container platform — Render, Fly, Cloud Run — has an <em>ephemeral</em> filesystem: the
/// disk is destroyed and recreated on every deploy, and these are the only bytes in the system that
/// are not in Postgres and therefore not in the database backup. There, the disk adapter does not
/// degrade, it loses the field's evidence on the next release.
/// </para>
/// <para>
/// Access keys are not here. They reach the S3 client from the environment
/// (<c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c>) or from a platform-provided role, so
/// there is nowhere in this section for a secret to be typed and committed.
/// </para>
/// </remarks>
public sealed class AttachmentOptions : IValidatableObject
{
    public const string SectionName = "Attachments";

    /// <summary>
    /// The directory content is stored under, when no <see cref="Bucket"/> is named.
    /// </summary>
    /// <remarks>
    /// A relative path resolves against the host's working directory, which is convenient in
    /// development and wrong in production: a deployment on a disk should give an absolute path on a
    /// volume that survives the container, and back it up alongside the database.
    /// </remarks>
    public string Root { get; init; } = string.Empty;

    /// <summary>
    /// The S3-compatible bucket content is stored in. Blank means the disk.
    /// </summary>
    /// <remarks>
    /// The bucket must already exist, and it must not have versioning or object-lock retention
    /// turned on: erasure deletes a photograph, and a bucket that answers a delete by keeping a
    /// previous version would leave a picture of somebody's home recoverable after they were told
    /// it was gone.
    /// </remarks>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>
    /// The store's endpoint — Cloudflare R2, Backblaze B2, MinIO, or anything else that speaks S3.
    /// Blank addresses Amazon S3 itself, which is named by <see cref="Region"/> instead.
    /// </summary>
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>
    /// The region to address and sign for. Required for Amazon S3; conventionally
    /// <c>us-east-1</c> for a store that has only one region.
    /// </summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Whether this deployment stores content in a bucket rather than on a disk.</summary>
    public bool UsesObjectStore => !string.IsNullOrWhiteSpace(Bucket);

    /// <summary>What the registration needs, as the shape Infrastructure understands.</summary>
    public AttachmentStorageSettings ToStorageSettings() =>
        UsesObjectStore
            ? AttachmentStorageSettings.InBucket(Bucket, ServiceUrl, Region)
            : AttachmentStorageSettings.OnDisk(Root);

    /// <summary>
    /// The rules a single <c>[Required]</c> cannot state: what is needed depends on which store
    /// this is.
    /// </summary>
    /// <remarks>
    /// All of it runs at startup, so a host with nowhere to put photographs — or with a bucket it
    /// has no way to address — fails before a technician finds out.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UsesObjectStore)
        {
            if (string.IsNullOrWhiteSpace(Root))
            {
                yield return new ValidationResult(
                    "Attachments:Root is required when no Attachments:Bucket is configured.",
                    [nameof(Root)]);
            }

            yield break;
        }

        if (string.IsNullOrWhiteSpace(ServiceUrl) && string.IsNullOrWhiteSpace(Region))
        {
            yield return new ValidationResult(
                "Attachments:Bucket needs either Attachments:ServiceUrl (any S3-compatible store) "
                + "or Attachments:Region (Amazon S3).",
                [nameof(ServiceUrl), nameof(Region)]);
        }

        // Caught here rather than at the first upload, where the SDK's own failure names neither
        // the setting nor this application.
        if (!string.IsNullOrWhiteSpace(ServiceUrl)
            && !Uri.TryCreate(ServiceUrl, UriKind.Absolute, out _))
        {
            yield return new ValidationResult(
                $"Attachments:ServiceUrl is not an absolute URL: '{ServiceUrl}'.",
                [nameof(ServiceUrl)]);
        }
    }
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

            // Validates the annotations *and* the IValidatableObject above — TryValidateObject with
            // validateAllProperties runs both, which is what lets the conditional rules live with
            // the options rather than in a second validator nobody finds.
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
