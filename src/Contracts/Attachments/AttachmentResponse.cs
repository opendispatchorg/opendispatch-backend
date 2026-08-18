namespace OpenDispatch.Contracts.Attachments;

/// <summary>
/// One capture, without its bytes. An element of <c>GET /jobs/{id}/attachments</c>.
/// </summary>
/// <remarks>
/// Metadata and a link rather than the content itself, for the reason the export gives: a job's
/// photographs are megabytes, and a client showing a list of them wants to know what is there
/// before deciding what to fetch. <paramref name="ContentUrl"/> is where the bytes are, so a client
/// composes no URLs of its own.
/// </remarks>
/// <param name="Id">The capture's id — the one its device generated.</param>
/// <param name="JobId">The job it was captured against.</param>
/// <param name="Kind">Photograph or signature.</param>
/// <param name="ContentType">What the bytes are, as the download will answer with.</param>
/// <param name="ByteLength">How big it is.</param>
/// <param name="CreatedAt">When it was captured.</param>
/// <param name="ContentUrl">Where to fetch the bytes, on this same API.</param>
public sealed record AttachmentResponse(
    Guid Id,
    Guid JobId,
    AttachmentKind Kind,
    string ContentType,
    long ByteLength,
    DateTimeOffset CreatedAt,
    string ContentUrl);
