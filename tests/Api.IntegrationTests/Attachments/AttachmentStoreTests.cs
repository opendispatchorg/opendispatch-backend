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
/// The metadata half of an attachment, and how it pairs with the bytes.
/// </summary>
/// <remarks>
/// <para>
/// What the store itself must do — round-trip, missing keys, deleting, overwriting — is
/// <see cref="AttachmentStorageContractTests"/>, written once and run against both adapters. What
/// is left here is the half that is about the database: the row records what was captured and where
/// its bytes are, one capture can only produce one row, and neither is visible to another tenant.
/// </para>
/// <para>
/// The one test that still spans both is the retry, which is the guarantee the technician app
/// leans on and is only meaningful when the row and the bytes are looked at together.
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
