using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Attachments.GetAttachmentContent;

/// <summary>
/// Finds the capture, then opens its bytes.
/// </summary>
/// <remarks>
/// <para>
/// Two lookups and two different misses. The row comes first, through a tenant-scoped repository —
/// so an attachment belonging to another organization is <em>not found</em> rather than found and
/// refused, the answer that leaks nothing. Only then is the store asked, and a store that has no
/// bytes for a row this database holds is its own failure with its own code
/// (<c>attachment.contentMissing</c>): the request was fine and the deployment is not.
/// </para>
/// <para>
/// The stream is handed out open. That is unusual for a handler and deliberate: reading a
/// photograph into a <c>byte[]</c> to hand back would put the whole capture in memory, which is
/// precisely what keeping content out of the database was for. The edge writes it to the response
/// and disposes it — see <c>AttachmentEndpoints</c>.
/// </para>
/// <para>
/// A query, so no transaction and nothing to roll back if the write to the client fails halfway.
/// </para>
/// </remarks>
internal sealed class GetAttachmentContentHandler(IAttachmentRepository attachments, IAttachmentStorage storage)
    : IRequestHandler<GetAttachmentContentQuery, Result<AttachmentContent>>
{
    public async Task<Result<AttachmentContent>> Handle(
        GetAttachmentContentQuery query,
        CancellationToken cancellationToken)
    {
        var attachment = await attachments
            .GetAsync(query.AttachmentId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return Result.Failure<AttachmentContent>(AttachmentErrors.NotFound(query.AttachmentId));
        }

        var content = await storage
            .OpenAsync(attachment.StorageKey, cancellationToken)
            .ConfigureAwait(false);

        if (content is null)
        {
            return Result.Failure<AttachmentContent>(AttachmentErrors.ContentMissing(query.AttachmentId));
        }

        return Result.Success(new AttachmentContent(
            content,
            attachment.ContentType,
            attachment.ByteLength,
            attachment.Kind));
    }
}
