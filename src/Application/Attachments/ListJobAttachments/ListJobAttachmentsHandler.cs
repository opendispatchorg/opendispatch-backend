using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Attachments.ListJobAttachments;

/// <summary>
/// Reads a job's captures, having first established that the job is this tenant's.
/// </summary>
/// <remarks>
/// The job lookup is not ceremony. An attachment references its job by id and cannot see it, so
/// "these are attachments on a job you may look at" is this handler's rule — and without it, asking
/// for a job that belongs to another organization would answer an empty list, which reads as "that
/// job has no photographs" rather than "that is not your job".
/// </remarks>
internal sealed class ListJobAttachmentsHandler(IAttachmentRepository attachments, IJobRepository jobs)
    : IRequestHandler<ListJobAttachmentsQuery, Result<IReadOnlyList<AttachmentSummary>>>
{
    public async Task<Result<IReadOnlyList<AttachmentSummary>>> Handle(
        ListJobAttachmentsQuery query,
        CancellationToken cancellationToken)
    {
        if (await jobs.GetAsync(query.JobId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Failure<IReadOnlyList<AttachmentSummary>>(JobErrors.NotFound(query.JobId));
        }

        var captured = await attachments
            .ListForJobAsync(query.JobId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AttachmentSummary> summaries =
        [
            .. captured.Select(attachment => new AttachmentSummary(
                attachment.Id,
                attachment.JobId,
                attachment.Kind,
                attachment.ContentType,
                attachment.ByteLength,
                attachment.CreatedAt)),
        ];

        return Result.Success(summaries);
    }
}
