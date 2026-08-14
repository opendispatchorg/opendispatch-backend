using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Attachments;

namespace OpenDispatch.Application.Attachments.UploadAttachment;

/// <summary>
/// Recognises a retry before touching the store, and writes the bytes before the row that points
/// at them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The idempotency check is the first thing that happens, and it is the whole guarantee.</strong>
/// A device that captured a photo, had the upload time out in the car park, and retried is asking
/// the same question twice; the second answer must be the first answer, not a second write. Nothing
/// downstream — the store, the domain factory — has to know a retry is a retry, because the retry
/// never reaches them.
/// </para>
/// <para>
/// <strong>The job is loaded and checked before anything is stored.</strong> An attachment
/// references its job by id and cannot see it (the same arrangement <c>Invoice</c> has), so "this
/// job is really this tenant's" is this handler's rule, not the aggregate's — and it is what turns a
/// cross-tenant upload into a 404 rather than a photograph written under another organization's
/// name.
/// </para>
/// <para>
/// <strong>Bytes before the row, and the row before nothing else.</strong> The blob is saved first;
/// only once that succeeds is the metadata staged, to be written when the transaction commits. A
/// request that fails between the two leaves an orphaned blob nothing points at — invisible and
/// harmless, step 43b's own entry names it and it stays unowned here too — rather than a row
/// pointing at content that was never written.
/// </para>
/// </remarks>
internal sealed class UploadAttachmentHandler(
    IAttachmentRepository attachments,
    IJobRepository jobs,
    IAttachmentStorage storage,
    ITenantContext tenant,
    IClock clock)
    : IRequestHandler<UploadAttachmentCommand, Result<UploadedAttachment>>
{
    public async Task<Result<UploadedAttachment>> Handle(
        UploadAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await attachments.GetAsync(command.AttachmentId, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Success(new UploadedAttachment(existing.StorageKey.Value));
        }

        var job = await jobs.GetAsync(command.JobId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return Result.Failure<UploadedAttachment>(JobErrors.NotFound(command.JobId));
        }

        var attachment = Attachment.Create(
            command.AttachmentId,
            tenant.OrgId,
            command.JobId,
            command.Kind,
            command.ContentType,
            command.ByteLength,
            clock.UtcNow);

        await storage.SaveAsync(attachment.StorageKey, command.Content, cancellationToken).ConfigureAwait(false);

        attachments.Add(attachment);

        return Result.Success(new UploadedAttachment(attachment.StorageKey.Value));
    }
}
