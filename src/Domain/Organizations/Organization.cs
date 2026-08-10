using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Organizations;

/// <summary>
/// A tenant: one field-service business running on the system.
/// </summary>
/// <remarks>
/// <para>
/// Almost empty on purpose. It exists now, before any tenant-aware feature does, because
/// every other aggregate already carries an <see cref="Identifiers.OrgId"/> and retrofitting
/// tenancy onto a system that grew up single-tenant means touching every table, every query
/// and every test at once.
/// </para>
/// <para>
/// It is the root the org scope hangs off, not a settings bag. Per-organization
/// configuration — working hours, scheduling weights, branding — belongs to whichever
/// feature needs it, added when that feature is built.
/// </para>
/// </remarks>
public sealed class Organization : AggregateRoot
{
    // Materialisation constructor — see the note on Job.
    private Organization() => Name = string.Empty;

    private Organization(OrgId id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>The tenant's identity — the value every business record in the system is scoped by.</summary>
    public OrgId Id { get; private set; }

    /// <summary>The business's name.</summary>
    public string Name { get; private set; }

    /// <summary>Registers a new tenant.</summary>
    /// <exception cref="DomainException">The organization has no name.</exception>
    public static Organization Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("An organization must have a name.");
        }

        return new Organization(OrgId.New(), name.Trim());
    }
}
