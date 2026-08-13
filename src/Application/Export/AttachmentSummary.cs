using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Export;

/// <summary>
/// A capture's metadata, as a reader sees it — <see cref="Domain.Attachments.Attachment"/>'s own
/// fields, with the storage key stated as a server id rather than as itself.
/// </summary>
/// <param name="Id">The device's own id for the capture.</param>
/// <param name="JobId">The job it was captured against.</param>
/// <param name="Kind">Photograph or signature.</param>
/// <param name="ServerId">
/// The handle its bytes are stored under — see <c>UploadedAttachment.ServerId</c>'s remarks for why
/// this rather than the storage key by name.
/// </param>
/// <param name="CreatedAt">When it was captured, by the device's clock.</param>
public sealed record AttachmentSummary(
    AttachmentId Id,
    JobId JobId,
    AttachmentKind Kind,
    string ServerId,
    DateTimeOffset CreatedAt);
