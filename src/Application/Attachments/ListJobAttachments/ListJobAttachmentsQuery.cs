using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Attachments.ListJobAttachments;

/// <summary>What was captured against one job.</summary>
/// <param name="JobId">The job.</param>
/// <remarks>
/// Unpaged, unlike the customer and job lists: this is bounded by one visit rather than by the age
/// of the business. See <c>IAttachmentRepository.ListForJobAsync</c>.
/// </remarks>
public sealed record ListJobAttachmentsQuery(JobId JobId) : IQuery<IReadOnlyList<AttachmentSummary>>;

/// <summary>One capture, without its bytes.</summary>
/// <param name="Id">The id the device gave it, and what a download is asked for by.</param>
/// <param name="JobId">The job it was captured against.</param>
/// <param name="Kind">Photograph or signature.</param>
/// <param name="ContentType">What the bytes are, as the download will answer with.</param>
/// <param name="ByteLength">How big it is, so a client can decide whether to fetch it now.</param>
/// <param name="CreatedAt">When it was captured.</param>
public sealed record AttachmentSummary(
    AttachmentId Id,
    JobId JobId,
    AttachmentKind Kind,
    string ContentType,
    long ByteLength,
    DateTimeOffset CreatedAt);
