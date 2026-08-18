namespace OpenDispatch.Infrastructure.Attachments;

/// <summary>
/// Which attachment store this deployment has, and what the adapter needs to reach it.
/// </summary>
/// <remarks>
/// <para>
/// Two shapes, and a value of this type is always exactly one of them: a directory, or a bucket.
/// The constructors are private and the factories name the two, so "a bucket with no name" and "a
/// disk with a service URL" are not states a caller can hand the registration — the same reason
/// <c>StorageKey</c> cannot be built from arbitrary text.
/// </para>
/// <para>
/// <strong>No credentials.</strong> Access keys reach the S3 client through the environment
/// (<c>AWS_ACCESS_KEY_ID</c> and friends, or an instance role), never through this type, because a
/// value that could carry a secret is a value that ends up in a configuration file somebody commits.
/// What is here is only the address of the store.
/// </para>
/// </remarks>
public sealed record AttachmentStorageSettings
{
    private AttachmentStorageSettings(string? root, string? bucket, string? serviceUrl, string? region)
    {
        Root = root;
        Bucket = bucket;
        ServiceUrl = serviceUrl;
        Region = region;
    }

    /// <summary>The directory content lives in, when this deployment stores it on a disk.</summary>
    public string? Root { get; }

    /// <summary>The bucket content lives in, when this deployment stores it in an object store.</summary>
    public string? Bucket { get; }

    /// <summary>
    /// The endpoint of an S3-compatible store, or <see langword="null"/> for Amazon's own.
    /// </summary>
    public string? ServiceUrl { get; }

    /// <summary>
    /// The region to address and sign for. Required by Amazon; any value will do for a store that
    /// has only one, which is why R2 and MinIO are conventionally given <c>us-east-1</c>.
    /// </summary>
    public string? Region { get; }

    /// <summary>Whether this deployment stores content in a bucket rather than on a disk.</summary>
    public bool IsObjectStore => Bucket is not null;

    /// <summary>Content on a local filesystem, under one directory.</summary>
    /// <param name="root">Where content lives. Created if it is not there.</param>
    public static AttachmentStorageSettings OnDisk(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        return new AttachmentStorageSettings(root, bucket: null, serviceUrl: null, region: null);
    }

    /// <summary>Content in an S3-compatible bucket.</summary>
    /// <param name="bucket">The bucket. It must already exist; the adapter never creates one.</param>
    /// <param name="serviceUrl">
    /// The store's endpoint, for anything that is not Amazon S3. Null addresses Amazon, which needs
    /// <paramref name="region"/> instead.
    /// </param>
    /// <param name="region">
    /// The region to address and sign for. Null is only valid alongside a
    /// <paramref name="serviceUrl"/>, where the adapter signs for <c>us-east-1</c> by convention.
    /// </param>
    public static AttachmentStorageSettings InBucket(string bucket, string? serviceUrl, string? region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);

        if (string.IsNullOrWhiteSpace(serviceUrl) && string.IsNullOrWhiteSpace(region))
        {
            throw new ArgumentException(
                "An object store needs either a service URL (any S3-compatible store) or a region (Amazon S3).",
                nameof(serviceUrl));
        }

        return new AttachmentStorageSettings(
            root: null,
            bucket,
            Blank(serviceUrl),
            Blank(region));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
