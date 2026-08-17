using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Attachments;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Attachments;

/// <summary>
/// What an attachment store must do, written once and run against every adapter that claims to be
/// one.
/// </summary>
/// <remarks>
/// <para>
/// There are two adapters now — a disk and a bucket — and "what a store must do" is one contract,
/// not two. Written per adapter, the pair drifts the moment somebody fixes a case in one of them:
/// the disk has always answered <see langword="null"/> for a key it has never seen, and whether the
/// bucket does is exactly the sort of thing nobody discovers until a restored backup is missing a
/// photograph. So the cases live here and each adapter is made to answer all of them.
/// </para>
/// <para>
/// Both subclasses go through <c>AddAttachmentStorage</c> rather than constructing an adapter, so
/// the registration's own choice — which settings mean which store — is under test too. That is the
/// line a deployment actually configures.
/// </para>
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Integration)]
public abstract class AttachmentStorageContractTests
{
    private readonly OrgId _tenant = OrgId.New();

    /// <summary>Where this adapter's content goes.</summary>
    protected abstract AttachmentStorageSettings Settings { get; }

    /// <summary>
    /// The bytes, through the port. Binary rather than text, because a photograph is not UTF-8 and
    /// a store that quietly re-encoded would pass a friendlier test.
    /// </summary>
    [Fact]
    public async Task RoundTripsContentByItsKey()
    {
        await using var services = Store();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var key = KeyFor(_tenant);
        var photograph = Photograph();

        await storage.SaveAsync(key, new MemoryStream(photograph), CancellationToken.None);

        await using var content = await storage.OpenAsync(key, CancellationToken.None);
        Assert.NotNull(content);

        using var read = new MemoryStream();
        await content.CopyToAsync(read);

        Assert.Equal(photograph, read.ToArray());
    }

    /// <summary>
    /// A metadata row whose blob is missing is something a caller can report on — a cleared volume,
    /// a half-restored backup. The answer is null, so the download is a 404 rather than a 500.
    /// </summary>
    [Fact]
    public async Task SaysNothingIsThereForAKeyItHasNeverSeen()
    {
        await using var services = Store();
        var storage = services.GetRequiredService<IAttachmentStorage>();

        var content = await storage.OpenAsync(KeyFor(_tenant), CancellationToken.None);

        Assert.Null(content);
    }

    /// <summary>
    /// One tenant's content cannot be reached by asking for another's key, because the tenant is
    /// part of the key — a device that guessed an attachment id still cannot name the object.
    /// </summary>
    [Fact]
    public async Task KeysContentByTenantSoOneCannotNameAnothersFile()
    {
        await using var services = Store();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var id = AttachmentId.From(Guid.NewGuid());

        await storage.SaveAsync(
            StorageKey.For(_tenant, id),
            new MemoryStream(Photograph()),
            CancellationToken.None);

        var elsewhere = await storage.OpenAsync(StorageKey.For(OrgId.New(), id), CancellationToken.None);

        Assert.Null(elsewhere);
    }

    /// <summary>
    /// Erasure is retried until it commits, so a delete whose first attempt already succeeded must
    /// still finish — otherwise the procedure is one nobody can complete.
    /// </summary>
    [Fact]
    public async Task DeletesContentAndCanBeAskedTwice()
    {
        await using var services = Store();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var key = KeyFor(_tenant);

        await storage.SaveAsync(key, new MemoryStream(Photograph()), CancellationToken.None);
        await storage.DeleteAsync(key, CancellationToken.None);

        Assert.Null(await storage.OpenAsync(key, CancellationToken.None));

        // The retry, and the delete of something that was never there — the same call either way.
        await storage.DeleteAsync(key, CancellationToken.None);
        await storage.DeleteAsync(KeyFor(_tenant), CancellationToken.None);
    }

    /// <summary>
    /// The case that separates deleting a key from deleting a prefix: two captures from one job
    /// sit beside each other under one organization, and erasing a customer removes the ones that
    /// are theirs.
    /// </summary>
    [Fact]
    public async Task DeletingOneLeavesItsNeighbour()
    {
        await using var services = Store();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var erased = KeyFor(_tenant);
        var kept = KeyFor(_tenant);

        await storage.SaveAsync(erased, new MemoryStream(Photograph()), CancellationToken.None);
        await storage.SaveAsync(kept, new MemoryStream(Photograph()), CancellationToken.None);

        await storage.DeleteAsync(erased, CancellationToken.None);

        Assert.Null(await storage.OpenAsync(erased, CancellationToken.None));

        await using var survivor = await storage.OpenAsync(kept, CancellationToken.None);
        Assert.NotNull(survivor);
    }

    /// <summary>
    /// The same capture arriving twice is a retried upload, not a conflict: the bytes are the same
    /// bytes, and the second write must leave the whole photograph rather than two halves.
    /// </summary>
    [Fact]
    public async Task OverwritesWhatIsAlreadyUnderTheKey()
    {
        await using var services = Store();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var key = KeyFor(_tenant);
        var photograph = Photograph();

        // A truncated first attempt, then the whole thing — which is what an upload interrupted in
        // a car park and retried on the way back looks like.
        await storage.SaveAsync(key, new MemoryStream(photograph[..64]), CancellationToken.None);
        await storage.SaveAsync(key, new MemoryStream(photograph), CancellationToken.None);

        await using var content = await storage.OpenAsync(key, CancellationToken.None);
        using var read = new MemoryStream();
        await content!.CopyToAsync(read);

        Assert.Equal(photograph, read.ToArray());
    }

    private static StorageKey KeyFor(OrgId tenant) =>
        StorageKey.For(tenant, AttachmentId.From(Guid.NewGuid()));

    // Not text: a photograph is bytes, and a store that re-encoded on the way through would pass a
    // test written with a string and fail on the first real capture.
    private static byte[] Photograph() =>
        [.. Encoding.UTF8.GetBytes("PNG\r\n\n"), 0x00, 0xFF, 0x7F, 0x01, .. new byte[512]];

    private ServiceProvider Store() =>
        new ServiceCollection()
            .AddLogging()
            .AddAttachmentStorage(_ => Settings)
            .BuildServiceProvider(validateScopes: true);
}

/// <summary>
/// The contract against the disk adapter — what a shop hosting this on its own machine runs.
/// </summary>
public sealed class LocalDiskAttachmentStorageTests : AttachmentStorageContractTests, IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"opendispatch-storage-{Guid.NewGuid():N}");

    protected override AttachmentStorageSettings Settings => AttachmentStorageSettings.OnDisk(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

/// <summary>
/// The same contract against a real S3 API — what a deployment on a platform with an ephemeral
/// container filesystem must run, because there the disk adapter loses every photograph on deploy.
/// </summary>
public sealed class ObjectStoreAttachmentStorageTests : AttachmentStorageContractTests, IAsyncLifetime
{
    private MinioFixture? _minio;

    protected override AttachmentStorageSettings Settings =>
        (_minio ?? throw new InvalidOperationException("The object store has not been started.")).Settings;

    public async Task InitializeAsync() => _minio = await MinioFixture.StartAsync();

    public async Task DisposeAsync()
    {
        if (_minio is not null)
        {
            await _minio.DisposeAsync();
        }
    }
}
