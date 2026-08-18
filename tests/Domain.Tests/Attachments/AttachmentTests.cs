using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests.Attachments;

/// <summary>
/// What an attachment refuses to be, and where it says its bytes are.
/// </summary>
/// <remarks>
/// Short, because an attachment has no lifecycle: it is captured and then it exists. The two things
/// worth holding down are the id — which is the whole idempotency story and comes from a device —
/// and the storage key, which is derived rather than given because it becomes a path on a disk.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class AttachmentTests
{
    private static readonly DateTimeOffset InTheField = new(2026, 8, 10, 14, 5, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void KeepsWhatWasCapturedAndDerivesWhereItGoes()
    {
        var id = AttachmentId.From(Guid.NewGuid());
        var org = OrgId.New();
        var job = JobId.New();

        var captured = Attachment.Create(id, org, job, AttachmentKind.Photo, "image/jpeg", 128L, InTheField);

        Assert.Equal(id, captured.Id);
        Assert.Equal(job, captured.JobId);
        Assert.Equal(InTheField, captured.CreatedAt);
        Assert.Equal(StorageKey.For(org, id), captured.StorageKey);

        // Nothing announces itself. A photograph is evidence, not a step in the job's life, and the
        // step-13 catalog names no event for one.
        Assert.Empty(captured.DomainEvents);
    }

    /// <summary>
    /// The one that matters. Every attachment captured without an id would be the same attachment,
    /// so the second would be discarded as a duplicate of a photograph of somewhere else — silently,
    /// which is how a technician's evidence disappears.
    /// </summary>
    [Fact]
    public void RefusesACaptureWithNoIdOfItsOwn()
    {
        Assert.Throws<DomainException>(() => Attachment.Create(
            new AttachmentId(Guid.Empty),
            OrgId.New(),
            JobId.New(),
            AttachmentKind.Photo,
            "image/jpeg",
            128L,
            InTheField));
    }

    [Fact]
    public void RefusesACaptureThatBelongsToNoJob()
    {
        Assert.Throws<DomainException>(() => Attachment.Create(
            AttachmentId.From(Guid.NewGuid()),
            OrgId.New(),
            new JobId(Guid.Empty),
            AttachmentKind.Photo,
            "image/jpeg",
            128L,
            InTheField));
    }

    /// <summary>
    /// The allow-list, which is the reason a content type is stored rather than guessed: a system
    /// that serves uploaded bytes back decides how a browser treats them.
    /// </summary>
    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/pdf")]
    [InlineData("")]
    public void RefusesAKindOfFileItDoesNotStore(string contentType)
    {
        Assert.Throws<DomainException>(() => Attachment.Create(
            AttachmentId.From(Guid.NewGuid()),
            OrgId.New(),
            JobId.New(),
            AttachmentKind.Photo,
            contentType,
            128L,
            InTheField));
    }

    /// <summary>A type is what it is whatever case or padding a client sent it in.</summary>
    [Theory]
    [InlineData("IMAGE/JPEG")]
    [InlineData("  image/png  ")]
    public void TakesAContentTypeHoweverItWasWritten(string contentType)
    {
        var captured = Attachment.Create(
            AttachmentId.From(Guid.NewGuid()),
            OrgId.New(),
            JobId.New(),
            AttachmentKind.Photo,
            contentType,
            128L,
            InTheField);

        Assert.Equal(contentType.Trim(), captured.ContentType);
    }

    [Fact]
    public void RefusesACaptureOfNothing()
    {
        Assert.Throws<DomainException>(() => Attachment.Create(
            AttachmentId.From(Guid.NewGuid()),
            OrgId.New(),
            JobId.New(),
            AttachmentKind.Photo,
            "image/jpeg",
            0L,
            InTheField));
    }

    [Fact]
    public void RefusesAKindOfAttachmentThatDoesNotExist()
    {
        Assert.Throws<DomainException>(() => Attachment.Create(
            AttachmentId.From(Guid.NewGuid()),
            OrgId.New(),
            JobId.New(),
            (AttachmentKind)9,
            "image/jpeg",
            128L,
            InTheField));
    }

    /// <summary>
    /// Two captures in the same organization go to two keys, and the same capture in two
    /// organizations goes to two more. The tenant being part of the key is what stops a device that
    /// guessed an attachment id from naming another organization's file.
    /// </summary>
    [Fact]
    public void GivesEveryCaptureAKeyOfItsOwn()
    {
        var org = OrgId.New();
        var elsewhere = OrgId.New();
        var id = AttachmentId.From(Guid.NewGuid());

        Assert.NotEqual(StorageKey.For(org, id), StorageKey.For(elsewhere, id));
        Assert.NotEqual(StorageKey.For(org, id), StorageKey.For(org, AttachmentId.From(Guid.NewGuid())));
    }

    /// <summary>
    /// A key can only be rebuilt from something this type produced. The refusals are the point: a
    /// key becomes a path, so anything looser is how a stored value ends up naming a file outside
    /// the store.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../../etc/passwd")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-uuid/00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000-0000-0000-0000-000000000000/../elsewhere")]
    [InlineData("00000000-0000-0000-0000-000000000000/00000000-0000-0000-0000-000000000000/extra")]
    public void RefusesTextThatIsNotAKeyItProduced(string text)
    {
        Assert.Throws<DomainException>(() => StorageKey.Parse(text));
    }

    [Fact]
    public void RebuildsAKeyThatCameOutOfStorage()
    {
        var key = StorageKey.For(OrgId.New(), AttachmentId.From(Guid.NewGuid()));

        Assert.Equal(key, StorageKey.Parse(key.Value));
    }
}
