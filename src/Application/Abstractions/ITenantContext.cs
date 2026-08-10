using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Whose data this request is about.
/// </summary>
/// <remarks>
/// <para>
/// Scoped to a request and populated at the edge from the authenticated principal (Document 2 §8,
/// step 45). Nothing below the edge ever decides which tenant it is working for; it asks.
/// </para>
/// <para>
/// This is why no repository takes an <see cref="OrgId"/>. Tenant scope is ambient: the
/// persistence layer applies it to every query through a global filter, so a query cannot forget
/// it and a handler cannot get it wrong. A parameter on every method would be a parameter someone
/// eventually passes the wrong value to.
/// </para>
/// <para>
/// It is deliberately read-only and non-nullable. There is no "no tenant" to represent here —
/// code that runs outside a tenant has no business reading tenant-scoped data, and an
/// implementation asked for an unresolved tenant should say so rather than hand back a value that
/// quietly matches nothing.
/// </para>
/// </remarks>
public interface ITenantContext
{
    /// <summary>The organization this request belongs to.</summary>
    OrgId OrgId { get; }
}
