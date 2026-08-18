using System.Globalization;
using Amazon.S3;
using Amazon.S3.Model;
using OpenDispatch.Infrastructure.Attachments;
using Testcontainers.Minio;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// A real S3-compatible object store, in a container, with one bucket ready.
/// </summary>
/// <remarks>
/// <para>
/// MinIO rather than a stub. The claim under test is that
/// <c>ObjectStoreAttachmentStorage</c> works against the S3 API a deployment will actually point
/// at — the status code a missing object comes back as, what a delete of nothing answers, whether a
/// path-style request is addressed correctly. A fake would only confirm our reading of the
/// protocol, which is the half that is not in doubt.
/// </para>
/// <para>
/// Started per test class rather than shared across the suite, the same choice
/// <c>DispatchHubBackplaneTests</c> made for its Redis: two classes need one, and a container every
/// run pays to start would be a tax on everybody for a fact two classes care about.
/// </para>
/// <para>
/// Each fixture makes its own bucket, so two classes running side by side cannot see each other's
/// objects even though they share the credentials below.
/// </para>
/// </remarks>
internal sealed class MinioFixture : IAsyncDisposable
{
    // Pinned like the PostGIS image the shared fixture uses: what the suite runs against should not
    // change under it when a package updates.
    private const string Image = "minio/minio:RELEASE.2025-04-22T22-12-26Z";

    // Fixed rather than random, and this is the reason: the SDK reads credentials from the process
    // environment (which is how a real deployment supplies them, and why nothing in this repository
    // can carry a key), and the environment is one thing shared by every test class in the run. Two
    // fixtures writing the same two values cannot disagree; two writing random ones would race, and
    // the loser would fail with a signature error nobody could reproduce.
    private const string AccessKey = "opendispatch-tests";
    private const string SecretKey = "opendispatch-tests-secret";

    private const ushort MinioPort = 9000;

    static MinioFixture()
    {
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", AccessKey);
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", SecretKey);
    }

    private readonly MinioContainer _container = new MinioBuilder(Image)
        .WithUsername(AccessKey)
        .WithPassword(SecretKey)
        .Build();

    private AmazonS3Client? _client;

    private MinioFixture()
    {
    }

    /// <summary>The bucket this fixture's tests store into, made fresh for each one.</summary>
    public string Bucket { get; } = $"opendispatch-{Guid.NewGuid():N}";

    /// <summary>What to hand <c>TestHost.Over</c> or the storage registration.</summary>
    public AttachmentStorageSettings Settings { get; private set; } =
        AttachmentStorageSettings.InBucket("unstarted", "http://localhost", "us-east-1");

    /// <summary>Starts the store and creates the bucket.</summary>
    public static async Task<MinioFixture> StartAsync()
    {
        var fixture = new MinioFixture();

        await fixture._container.StartAsync();

        var endpoint = string.Create(
            CultureInfo.InvariantCulture,
            $"http://{fixture._container.Hostname}:{fixture._container.GetMappedPublicPort(MinioPort)}");

        fixture.Settings = AttachmentStorageSettings.InBucket(fixture.Bucket, endpoint, "us-east-1");

        fixture._client = new AmazonS3Client(
            AccessKey,
            SecretKey,
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });

        // The adapter never creates a bucket — a deployment's bucket is made once, with its own
        // policy and lifecycle, by whoever owns the account. So the fixture stands in for that.
        await fixture._client.PutBucketAsync(new PutBucketRequest { BucketName = fixture.Bucket });

        return fixture;
    }

    /// <summary>Whether the store holds an object under a key — asked directly, not through the port.</summary>
    /// <remarks>
    /// A test that proved a photograph was gone by asking the same adapter that deleted it would be
    /// proving the adapter agrees with itself. This asks the bucket.
    /// </remarks>
    public async Task<bool> ExistsAsync(string storageKey)
    {
        try
        {
            await Client.GetObjectMetadataAsync(Bucket, storageKey);
            return true;
        }
        catch (AmazonS3Exception missing) when (missing.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        await _container.DisposeAsync();
    }

    private AmazonS3Client Client =>
        _client ?? throw new InvalidOperationException("The fixture has not been started.");
}
