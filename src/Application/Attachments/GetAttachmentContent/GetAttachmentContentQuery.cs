using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Attachments.GetAttachmentContent;

/// <summary>The bytes of one capture.</summary>
/// <param name="AttachmentId">Which capture.</param>
public sealed record GetAttachmentContentQuery(AttachmentId AttachmentId) : IQuery<AttachmentContent>;

/// <summary>
/// An open stream over a capture, and what a caller needs to serve it.
/// </summary>
/// <param name="Content">
/// The bytes, still in the store — <strong>the caller owns this stream and must dispose it</strong>.
/// It is deliberately not read into memory: a photograph is megabytes, and the point of keeping
/// content out of the database is not to hold it in the process either.
/// </param>
/// <param name="ContentType">What the bytes are, as recorded when they were stored.</param>
/// <param name="ByteLength">How many there are, as recorded when they were stored.</param>
/// <param name="Kind">Photograph or signature, for naming the download.</param>
public sealed record AttachmentContent(
    Stream Content,
    string ContentType,
    long ByteLength,
    Domain.Attachments.AttachmentKind Kind);
