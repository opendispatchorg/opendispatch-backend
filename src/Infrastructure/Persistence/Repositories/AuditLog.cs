using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auditing;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAuditLog"/>
/// <remarks>
/// Two lines, and the shape is the point: it stages like a repository and is committed by the same
/// unit of work as the work it describes, so the entry and the act land together or not at all.
/// </remarks>
internal sealed class AuditLog(AppDbContext context) : IAuditLog
{
    public void Record(AuditEntry entry) => context.AuditEntries.Add(entry);
}
