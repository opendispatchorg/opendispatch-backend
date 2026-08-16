using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Auditing;

/// <summary>
/// One thing somebody did: who, to what, in which organization, and when.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What it records is the act, not the payload.</strong> The command's name and the
/// identities it named — never the values it carried. That is a deliberate limit rather than an
/// omission: a log holding a customer's name and phone number would be a second, append-only copy of
/// the personal data an erasure request is supposed to remove, and erasing a customer would mean
/// rewriting history to keep a promise. This way the two are compatible — the trail says
/// <em>who cancelled job X at 14:02</em> and holds nothing an erasure has to reach into.
/// </para>
/// <para>
/// It is not an aggregate and not in the domain: an audit entry is a fact about the system being
/// used, with no invariant beyond being complete — the same reasoning that keeps <c>SyncOpRecord</c>
/// out of the domain. It sits beside its port rather than in <c>Abstractions</c> for the same reason
/// <c>SyncOpRecord</c> does: that folder is ports, and a port may not traffic in raw identities,
/// which a record of what happened necessarily does.
/// </para>
/// </remarks>
public sealed class AuditEntry
{
    // Materialisation constructor — see the note on Job.
    private AuditEntry()
    {
        Action = string.Empty;
        Targets = string.Empty;
    }

    private AuditEntry(
        Guid id,
        OrgId orgId,
        UserId? userId,
        string? username,
        string action,
        string targets,
        DateTimeOffset at)
    {
        Id = id;
        OrgId = orgId;
        UserId = userId;
        Username = username;
        Action = action;
        Targets = targets;
        At = at;
    }

    /// <summary>This entry's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The organization the act happened in.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>Who did it, or <see langword="null"/> when the system acted on its own.</summary>
    public UserId? UserId { get; private set; }

    /// <summary>
    /// What they signed in as, kept alongside the id.
    /// </summary>
    /// <remarks>
    /// Denormalised on purpose: a trail that could only name a user by id stops being readable the
    /// day that user is deleted, and "who was this" is the question the trail exists to answer.
    /// </remarks>
    public string? Username { get; private set; }

    /// <summary>What was done, by the command's name — <c>CancelJob</c>, <c>EraseCustomer</c>.</summary>
    public string Action { get; private set; }

    /// <summary>The identities the act named, as a JSON object of property name to id.</summary>
    public string Targets { get; private set; }

    /// <summary>When it happened, by the server's clock.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>Records an act.</summary>
    public static AuditEntry For(
        OrgId orgId,
        UserId? userId,
        string? username,
        string action,
        string targets,
        DateTimeOffset at) =>
        new(Guid.NewGuid(), orgId, userId, username, action, targets, at);
}
