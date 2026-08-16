using OpenDispatch.Application.Auditing;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Where the record of what was done goes.
/// </summary>
/// <remarks>
/// Shaped like <c>ISyncOpStore</c> rather than a repository, and for the same reason: an entry is
/// staged and committed by the unit of work that committed the work it describes, so the act and the
/// record of it are one write. An audit trail that could disagree with the database would be worse
/// than none.
/// </remarks>
public interface IAuditLog
{
    /// <summary>Stages an entry. It is written when the unit of work saves.</summary>
    void Record(AuditEntry entry);
}
