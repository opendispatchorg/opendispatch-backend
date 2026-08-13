namespace OpenDispatch.Contracts.Attachments;

/// <summary>
/// The metadata half of <c>POST /jobs/{id}/attachments</c> — the multipart form's fields beside the
/// binary itself.
/// </summary>
/// <param name="AttachmentId">The id the device generated for the capture — the idempotency key.</param>
/// <param name="JobId">
/// The job it was captured against. Carried here as the build text names it, but the route's own
/// <c>{id}</c> is what the endpoint actually acts on — the same convention every other
/// <c>/jobs/{id}/...</c> route in this API already follows, of not trusting a body to restate an id
/// the URL already carries. The two are checked against each other; a mismatch is refused rather
/// than silently resolved one way.
/// </param>
/// <param name="Kind">Photograph or signature.</param>
public sealed record UploadAttachmentRequest(Guid AttachmentId, Guid JobId, AttachmentKind Kind);
