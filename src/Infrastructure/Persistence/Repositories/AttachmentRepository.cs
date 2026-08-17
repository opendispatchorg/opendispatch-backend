using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Attachments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAttachmentRepository"/>
internal sealed class AttachmentRepository(AppDbContext context) : IAttachmentRepository
{
    public Task<Attachment?> GetAsync(AttachmentId id, CancellationToken ct) =>
        context.Attachments.FirstOrDefaultAsync(attachment => attachment.Id == id, ct);

    public void Add(Attachment attachment) => context.Attachments.Add(attachment);

    public void Remove(Attachment attachment) => context.Attachments.Remove(attachment);

    /// <remarks>
    /// Oldest first: a visit's photographs are read in the order they were taken, which is the
    /// order they tell the story of the job in.
    /// </remarks>
    public async Task<IReadOnlyList<Attachment>> ListForJobAsync(JobId job, CancellationToken ct) =>
        await context.Attachments
            .Where(attachment => attachment.JobId == job)
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .ToListAsync(ct);

    /// <remarks>
    /// By capture instant: an export reads a shop's records in the order they happened. Streamed,
    /// like the rest of the export's reads.
    /// <para>
    /// <strong>No-tracking, and that is not an optimisation.</strong> A tracked stream puts every row
    /// it hands out into the change tracker and holds it there until the request ends — so an export
    /// that streams precisely so a shop's history need not be held in memory would hold all of it
    /// anyway, one identity map at a time. Measured on a year of history: the peak came down by
    /// roughly a third. Nothing saves a projection, so there is nothing to track for.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<Attachment> StreamAsync(CancellationToken ct) =>
        context.Attachments
            .AsNoTracking()
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .AsAsyncEnumerable();
}
