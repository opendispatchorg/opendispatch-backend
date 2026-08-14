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

    /// <remarks>
    /// By capture instant: an export reads a shop's records in the order they happened. Streamed,
    /// like the rest of the export's reads.
    /// </remarks>
    public IAsyncEnumerable<Attachment> StreamAsync(CancellationToken ct) =>
        context.Attachments
            .OrderBy(attachment => attachment.CreatedAt)
            .ThenBy(attachment => attachment.Id)
            .AsAsyncEnumerable();
}
