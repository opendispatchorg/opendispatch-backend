using Microsoft.EntityFrameworkCore;
using OpenDispatch.Domain.Common;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// What is true of every aggregate root, applied to all of them rather than restated in each
/// configuration.
/// </summary>
/// <remarks>
/// A sweep rather than a base class for the per-aggregate configurations, because the two rules
/// here are not the kind that should be possible to forget. A root whose configuration omitted
/// the concurrency token would persist and reload perfectly, and lose a dispatcher's edit the
/// first time two of them dragged the same job — a bug with no failing test and no error, found
/// in production or not at all. Inherited-from-a-base would have been one missing colon away
/// from exactly that.
/// </remarks>
internal static class AggregateRootConventions
{
    /// <summary>
    /// Applies the aggregate-root rules to every root already in the model. Runs after the
    /// per-aggregate configurations so nothing can be added behind its back.
    /// </summary>
    internal static void Apply(ModelBuilder modelBuilder)
    {
        var roots = modelBuilder.Model
            .GetEntityTypes()
            .Where(entityType => entityType.ClrType.IsAssignableTo(typeof(AggregateRoot)))
            .ToList();

        foreach (var root in roots)
        {
            var builder = modelBuilder.Entity(root.ClrType);

            // Events raised but not yet published are pending work, not state. They live for
            // the length of a transaction and are drained by the dispatcher (step 31); a column
            // for them would be a queue in the wrong place.
            builder.Ignore(nameof(AggregateRoot.DomainEvents));

            // Optimistic concurrency (Document 2 §6): the version a row was read at goes into
            // the WHERE clause of its UPDATE, so the second of two writers to the same job is
            // told rather than silently overwriting the first.
            builder.Property(nameof(AggregateRoot.Version)).IsConcurrencyToken();
        }
    }
}
