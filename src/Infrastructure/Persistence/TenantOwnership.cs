using System.Reflection;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// Which property says who owns a row.
/// </summary>
/// <remarks>
/// <para>
/// One definition of the rule, because two things now depend on it: <see cref="TenantQueryFilters"/>
/// scopes reads by it, and <c>DomainEventInterceptor</c> stamps an outbox row with it. Two copies
/// would be two rules the day somebody changed one — and the failure that produces is a message
/// delivered under the wrong tenant, which is the worst shape a bug can take here.
/// </para>
/// <para>
/// The rule is about the type rather than a list: an entity with exactly one property of type
/// <see cref="OrgId"/> is owned through it. That covers the business aggregates through their
/// <c>OrgId</c>, and <c>Organization</c> through its own <c>Id</c> — an organization is the one row
/// whose tenant is itself.
/// </para>
/// <para>
/// A <em>nullable</em> <c>OrgId</c> is deliberately not a match. <c>OutboxMessage</c> carries one so
/// the sweep can tell whose reaction a message is, while still being readable across tenants by a
/// dispatcher that has resolved nobody — see <see cref="TenantQueryFilters"/>.
/// </para>
/// </remarks>
internal static class TenantOwnership
{
    /// <summary>The property that says which organization owns an instance, if there is one.</summary>
    /// <param name="clrType">The entity type.</param>
    /// <exception cref="InvalidOperationException">The type has more than one, so ownership is ambiguous.</exception>
    internal static PropertyInfo? PropertyOf(Type clrType)
    {
        var candidates = clrType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(OrgId))
            .ToList();

        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],

            // Two ways to say which tenant owns a row is one way too many: whichever this picked
            // would be a coin toss made at model build, and the wrong side of it is a leak.
            _ => throw new InvalidOperationException(
                $"'{clrType.Name}' has more than one {nameof(OrgId)} property, so which one scopes "
                + "it is ambiguous. Tenant-owned types carry exactly one."),
        };
    }

    /// <summary>The organization that owns an instance, read through <see cref="PropertyOf"/>.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>Its owning organization, or <see langword="null"/> if the type has no such property.</returns>
    internal static OrgId? Of(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return PropertyOf(entity.GetType())?.GetValue(entity) as OrgId?;
    }
}
