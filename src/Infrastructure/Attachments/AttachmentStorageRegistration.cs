using Amazon;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Attachments;

/// <summary>
/// Registration for attachment content storage: a disk, or a bucket.
/// </summary>
/// <remarks>
/// The one place the two adapters are chosen between, and the only place in the system that knows
/// there are two. Everything above <see cref="IAttachmentStorage"/> — the upload handler, the
/// download, the erasure — is written once and runs unchanged against either.
/// </remarks>
public static class AttachmentStorageRegistration
{
    /// <summary>
    /// Registers whichever adapter this deployment's settings describe.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="settings">
    /// Reads the store's address once the container is built, for the same reason the connection
    /// string is a factory: the host validates its configuration on start, and the validated value
    /// does not exist yet when registration runs.
    /// </param>
    /// <remarks>
    /// A singleton either way. The disk adapter holds a path; the bucket adapter holds an S3 client,
    /// which is itself designed to be long-lived and shared — a client per request would leak a
    /// connection pool per request. Both are safe to call from several requests at once, and the
    /// container disposes the bucket adapter (and with it the client) at shutdown.
    /// </remarks>
    public static IServiceCollection AddAttachmentStorage(
        this IServiceCollection services,
        Func<IServiceProvider, AttachmentStorageSettings> settings) =>
        services.AddSingleton<IAttachmentStorage>(provider =>
        {
            var store = settings(provider);

            return store.IsObjectStore
                ? new ObjectStoreAttachmentStorage(
                    ClientFor(store),
                    store.Bucket!,
                    provider.GetRequiredService<ILogger<ObjectStoreAttachmentStorage>>())
                : new LocalDiskAttachmentStorage(
                    store.Root!,
                    provider.GetRequiredService<ILogger<LocalDiskAttachmentStorage>>());
        });

    /// <summary>
    /// The S3 client for a bucket, addressed path-style so one adapter serves every S3-compatible
    /// store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Credentials are not here, deliberately.</strong> The client is constructed without
    /// them, which makes the SDK resolve them from the environment: <c>AWS_ACCESS_KEY_ID</c> and
    /// <c>AWS_SECRET_ACCESS_KEY</c>, or a container/instance role where the platform provides one.
    /// A key that arrived through <c>IConfiguration</c> would be a key that can be typed into
    /// <c>appsettings.json</c> and committed, and this repository already refuses to start with a
    /// published signing key for the same reason.
    /// </para>
    /// <para>
    /// A named endpoint means an S3-compatible store, so the region is only what requests are
    /// signed for and <c>us-east-1</c> is the convention every one of them accepts. No endpoint
    /// means Amazon, where the region names the endpoint and is therefore required.
    /// </para>
    /// </remarks>
    private static AmazonS3Client ClientFor(AttachmentStorageSettings store)
    {
        var config = new AmazonS3Config
        {
            // Bucket in the path rather than in the hostname. Virtual-host addressing needs a DNS
            // name per bucket, which a MinIO on a laptop or in a test container does not have.
            ForcePathStyle = true,
        };

        if (store.ServiceUrl is not null)
        {
            config.ServiceURL = store.ServiceUrl;
            config.AuthenticationRegion = store.Region ?? "us-east-1";
        }
        else
        {
            // GetBySystemName never returns null — an unknown name yields a region built from it,
            // which is what makes a new AWS region usable without an SDK update, and what makes a
            // typo a failure at the first request rather than at startup.
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(store.Region!);
        }

        return new AmazonS3Client(config);
    }
}
