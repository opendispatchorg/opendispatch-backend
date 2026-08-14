using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Attachments;

/// <summary>
/// The two halves of an attachment, against a real database and a real disk.
/// </summary>
/// <remarks>
/// <para>
/// The metadata is an ordinary tenant-scoped row and is covered as such. What earns its own tests
/// is the pair of guarantees the technician app leans on: the bytes come back exactly as they went
/// in, and the same capture arriving twice does not become two attachments — which is what a
/// retried upload over a bad connection looks like.
/// </para>
/// <para>
/// There is no upload command yet (step 50b), so these exercise the port and the store directly.
/// That is deliberate rather than a shortcut: this step's deliverable is the storage, and its
/// contract is what step 50b will be written against.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AttachmentStoreTests
{
    private static readonly DateTimeOffset InTheField = new(2026, 8, 10, 14, 5, 0, TimeSpan.FromHours(-5));

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public AttachmentStoreTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task KeepsWhatWasCapturedAndWhereItsBytesAre()
    {
        await using var services = BuildHost();
        var job = JobId.New();
        var captured = Attachment.Create(
            AttachmentId.From(Guid.NewGuid()),
            _tenant,
            job,
            AttachmentKind.Signature,
            "image/jpeg",
            128L,
            InTheField);

        await AddAsync(services, captured);

        await using var read = _postgres.NewContext(_tenant);
        var stored = await read.Attachments.SingleAsync(row => row.Id == captured.Id);

        Assert.Equal(job, stored.JobId);
        Assert.Equal(AttachmentKind.Signature, stored.Kind);
        Assert.Equal(InTheField, stored.CreatedAt);

        // Derived from the tenant and the attachment, never from anything a device sent — which is
        // what makes the key safe to turn into a path.
        Assert.Equal(StorageKey.For(_tenant, captured.Id), stored.StorageKey);
    }

    /// <summary>
    /// The bytes, through the port a bucket adapter will implement later. Binary rather than text,
    /// because a photograph is not UTF-8 and a store that quietly re-encoded would pass a
    /// friendlier test.
    /// </summary>
    [Fact]
    public async Task RoundTripsContentByItsKey()
    {
        await using var services = BuildHost();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var key = StorageKey.For(_tenant, AttachmentId.From(Guid.NewGuid()));
        var photograph = Photograph();

        await storage.SaveAsync(key, new MemoryStream(photograph), CancellationToken.None);

        await using var content = await storage.OpenAsync(key, CancellationToken.None);
        Assert.NotNull(content);

        using var read = new MemoryStream();
        await content.CopyToAsync(read);

        Assert.Equal(photograph, read.ToArray());
    }

    /// <summary>
    /// A metadata row whose blob is missing is something a caller can report on. Nothing produces
    /// that state today, and the day something does — a cleared volume, a half-restored backup —
    /// the answer is a 404 rather than a 500.
    /// </summary>
    [Fact]
    public async Task SaysNothingIsThereForAKeyItHasNeverSeen()
    {
        await using var services = BuildHost();
        var storage = services.GetRequiredService<IAttachmentStorage>();

        var content = await storage.OpenAsync(
            StorageKey.For(_tenant, AttachmentId.From(Guid.NewGuid())),
            CancellationToken.None);

        Assert.Null(content);
    }

    /// <summary>
    /// The step's third requirement, and what a retried upload looks like: the same capture arrives
    /// twice. The bytes are simply overwritten with themselves; the row cannot be written twice,
    /// because the device's id is the primary key.
    /// </summary>
    [Fact]
    public async Task StoresTheSameCaptureTwiceWithoutMakingTwoOfIt()
    {
        await using var services = BuildHost();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var id = AttachmentId.From(Guid.NewGuid());
        var captured = Attachment.Create(id, _tenant, JobId.New(), AttachmentKind.Photo, "image/jpeg", 128L, InTheField);
        var photograph = Photograph();

        await storage.SaveAsync(captured.StorageKey, new MemoryStream(photograph), CancellationToken.None);
        await AddAsync(services, captured);

        // The retry: the same bytes under the same key, then the same row.
        await storage.SaveAsync(captured.StorageKey, new MemoryStream(photograph), CancellationToken.None);

        using (var scope = services.ActingAs(_tenant))
        {
            var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentRepository>();

            // What an upload asks before it does anything, and the answer that makes the retry a
            // no-op rather than a second attachment.
            Assert.NotNull(await attachments.GetAsync(id, CancellationToken.None));
        }

        await using var read = _postgres.NewContext(_tenant);
        Assert.Equal(1, await read.Attachments.CountAsync(row => row.Id == id));

        await using var content = await storage.OpenAsync(captured.StorageKey, CancellationToken.None);
        using var reread = new MemoryStream();
        await content!.CopyToAsync(reread);

        Assert.Equal(photograph, reread.ToArray());
    }

    /// <summary>
    /// A second row for one capture is refused by the database, not by anybody remembering to look
    /// first — which is what holds when two retries arrive at once.
    /// </summary>
    [Fact]
    public async Task RefusesASecondRowForOneCapture()
    {
        await using var services = BuildHost();
        var id = AttachmentId.From(Guid.NewGuid());

        await AddAsync(services, Attachment.Create(id, _tenant, JobId.New(), AttachmentKind.Photo, "image/jpeg", 128L, InTheField));

        // DuplicateRecordException, not the provider's own: the unit of work translates a unique
        // violation into the port's word for it, so a caller answers 409 rather than 500.
        await Assert.ThrowsAsync<DuplicateRecordException>(() =>
            AddAsync(services, Attachment.Create(id, _tenant, JobId.New(), AttachmentKind.Signature, "image/jpeg", 128L, InTheField)));
    }

    [Fact]
    public async Task KeepsOneTenantsAttachmentsOutOfAnothersSight()
    {
        await using var services = BuildHost();
        var captured = Attachment.Create(
            AttachmentId.From(Guid.NewGuid()),
            _tenant,
            JobId.New(),
            AttachmentKind.Photo,
            "image/jpeg",
            128L,
            InTheField);

        await AddAsync(services, captured);

        using var elsewhere = services.ActingAs(OrgId.New());
        var found = await elsewhere.ServiceProvider
            .GetRequiredService<IAttachmentRepository>()
            .GetAsync(captured.Id, CancellationToken.None);

        Assert.Null(found);
    }

    /// <summary>
    /// One tenant's content cannot be reached by asking for another's key, because the tenant is
    /// part of the key — a device that guessed an attachment id still cannot name the file.
    /// </summary>
    [Fact]
    public async Task KeysContentByTenantSoOneCannotNameAnothersFile()
    {
        await using var services = BuildHost();
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var id = AttachmentId.From(Guid.NewGuid());

        await storage.SaveAsync(
            StorageKey.For(_tenant, id),
            new MemoryStream(Photograph()),
            CancellationToken.None);

        var elsewhere = await storage.OpenAsync(StorageKey.For(OrgId.New(), id), CancellationToken.None);

        Assert.Null(elsewhere);
    }

    // Not text: a photograph is bytes, and a store that re-encoded on the way through would pass a
    // test written with a string and fail on the first real capture.
    private static byte[] Photograph() =>
        [.. Encoding.UTF8.GetBytes("PNG\r\n\n"), 0x00, 0xFF, 0x7F, 0x01, .. new byte[512]];

    private async Task AddAsync(ServiceProvider services, Attachment attachment)
    {
        using var scope = services.ActingAs(_tenant);

        scope.ServiceProvider.GetRequiredService<IAttachmentRepository>().Add(attachment);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);
}
