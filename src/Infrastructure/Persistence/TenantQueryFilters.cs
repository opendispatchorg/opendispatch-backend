using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// Scopes every tenant-owned entity type to the current organization.
/// </summary>
/// <remarks>
/// <para>
/// A sweep, not a line in each configuration, because this is the one rule in the system that has
/// to hold for aggregates nobody has written yet. A configuration that forgot its filter would
/// pass every test it has, and the bug it caused would be one tenant reading another's jobs.
/// </para>
/// <para>
/// The rule it applies is deliberately about the type rather than a list: an entity type with a
/// property of type <see cref="OrgId"/> is scoped by that property. That covers the five business
/// aggregates through their <c>OrgId</c>, and <c>Organization</c> through its own <c>Id</c> — an
/// organization is the one row whose tenant is itself. Owned children have no such property and
/// need none; they are loaded through a root that is already filtered.
/// </para>
/// <para>
/// Filters constrain reads, not writes (Document 2 §8 says "on the read path" for this reason).
/// Nothing here stops a handler stamping the wrong <see cref="OrgId"/> onto a new row; what it
/// stops is anyone ever seeing a row that is not theirs.
/// </para>
/// <para>
/// <strong><c>OutboxMessage</c> is the one deliberate exception, and it is load-bearing.</strong> It
/// carries an owning organization so the sweep can resolve a tenant before publishing — without
/// that, every reaction the background sweep touched threw and became a poison row — but the sweep
/// must also be able to <em>find</em> work for tenants nobody has resolved yet. A filter here would
/// make it read nothing at all. Its property is therefore a nullable <see cref="OrgId"/>, which
/// <see cref="TenantOwnership.PropertyOf"/> does not match; the scoping it needs is applied
/// explicitly by <c>OutboxSweep</c>, per claim.
/// </para>
/// </remarks>
internal static class TenantQueryFilters
{
    internal static void Apply(ModelBuilder modelBuilder, AppDbContext context)
    {
        // Read once: the property is on the context instance, so EF re-reads it per query rather
        // than baking one tenant's id into the cached model. That is what makes a single model
        // safe to share between requests belonging to different organizations.
        var currentTenant = Expression.Property(
            Expression.Constant(context),
            nameof(AppDbContext.CurrentOrgId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var scope = TenantOwnership.PropertyOf(entityType.ClrType);

            if (scope is null)
            {
                continue;
            }

            var entity = Expression.Parameter(entityType.ClrType, "entity");

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(
                Expression.Lambda(
                    Expression.Equal(Expression.Property(entity, scope), currentTenant),
                    entity));
        }
    }

}
