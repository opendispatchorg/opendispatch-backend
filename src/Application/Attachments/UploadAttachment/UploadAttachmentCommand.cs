using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Attachments.UploadAttachment;

/// <summary>
/// A technician's phone handing over one capture: a photo or a signature, and the job it belongs
/// to.
/// </summary>
/// <param name="AttachmentId">The id the device generated for it — the idempotency key.</param>
/// <param name="JobId">The job it was captured against.</param>
/// <param name="Kind">Photograph or signature.</param>
/// <param name="ContentType">
/// What the bytes are, as the request declared. Checked against <c>Attachment.AllowedContentTypes</c>
/// — by the validator, so a phone sending something this system does not store is told so, and by
/// the domain, so nothing else can write one either.
/// </param>
/// <param name="ByteLength">How many bytes there are, as the request declared.</param>
/// <param name="Content">
/// The bytes, read from wherever the caller's stream is positioned. Not buffered by this command —
/// see <c>UploadAttachmentHandler</c> for where it is actually read.
/// </param>
/// <remarks>
/// <para>
/// Idempotent by construction, not by a status code: a device that captures a photo, has the
/// upload time out in the car park, and retries sends the same <see cref="AttachmentId"/> — the
/// handler recognises it and answers with what is already stored rather than writing the bytes
/// twice.
/// </para>
/// <para>
/// No captured-at instant, unlike every other field op this phase carries one for. The build text
/// names four parameters and a device's own clock is not among them; <c>Attachment.Create</c> is
/// given the server's clock instead. Worth knowing, not silently different: <c>Attachment
/// .CreatedAt</c>'s own remarks prefer the device's instant for the reason a photograph is evidence
/// of what a site looked like at the time, and this command cannot supply one.
/// </para>
/// </remarks>
public sealed record UploadAttachmentCommand(
    AttachmentId AttachmentId,
    JobId JobId,
    AttachmentKind Kind,
    string ContentType,
    long ByteLength,
    Stream Content) : ICommand<UploadedAttachment>;
