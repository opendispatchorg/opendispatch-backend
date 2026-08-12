using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// Stamps every row with the transaction that last wrote it, which is what a sync cursor is a
/// position in.
/// </summary>
/// <remarks>
/// <para>
/// A device asks "what has changed since?", so something has to order changes. Postgres already
/// does: every row carries the id of the transaction that wrote it, and
/// <c>pg_current_xact_id()</c> exposes it as a 64-bit number that only increases. This copies
/// that number into an ordinary <c>change_seq</c> column, so the ordering is a bigint an index
/// and a <c>WHERE</c> clause can use rather than a system column with wraparound arithmetic
/// around it.
/// </para>
/// <para>
/// The value is written by a database trigger (see the migration), not by an interceptor or a
/// handler, and that is the decision this whole mechanism rests on. It has to be the writing
/// transaction's own id, taken inside that transaction — a number allocated a moment before the
/// rows are committed can be handed to a device as a cursor while the rows are still invisible,
/// and those rows are then never sent to it again. A <c>BEFORE INSERT OR UPDATE</c> trigger is
/// the one place that cannot be got wrong, because it runs inside the writing statement whoever
/// wrote it: this application, a migration, the seeder, or somebody at a psql prompt.
/// </para>
/// <para>
/// Every table gets it, including the tables owned children live in. "Everything a tenant owns
/// knows when it last changed" is a rule with no exceptions to remember; a list of syncable
/// tables would be one, and the missing entry would surface as a phone quietly holding a stale
/// address.
/// </para>
/// </remarks>
internal static class ChangeStamps
{
    /// <summary>
    /// The stamp's property name. <c>change_seq</c> once the naming convention has had it.
    /// </summary>
    /// <remarks>
    /// A shadow property rather than a member of anything: no aggregate has business knowing
    /// which transaction wrote it, and a <c>Job</c> with a persistence counter on it would be a
    /// domain type answering to the sync protocol. The pull path reads it with
    /// <c>EF.Property&lt;long&gt;(entity, PropertyName)</c>.
    /// </remarks>
    internal const string PropertyName = "ChangeSeq";

    /// <summary>The value the trigger writes, and the default that documents it in the schema.</summary>
    /// <remarks>
    /// Through <c>text</c> because <c>xid8</c> has no direct cast to <c>bigint</c>. The value is a
    /// 64-bit transaction id with the epoch in its high bits, so unlike the 32-bit <c>xmin</c>
    /// system column it does not wrap and can be compared with a plain <c>&gt;=</c>.
    /// </remarks>
    internal const string CurrentTransactionSql = "pg_current_xact_id()::text::bigint";

    /// <summary>
    /// The watermark: the oldest transaction that could still be in flight, and therefore the
    /// lowest stamp a row not yet visible to this connection can have.
    /// </summary>
    internal const string WatermarkSql = "pg_snapshot_xmin(pg_current_snapshot())::text::bigint";

    /// <summary>
    /// Adds the stamp to every table in the model. Runs after the per-aggregate configurations,
    /// so a table added later cannot arrive without one.
    /// </summary>
    internal static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!HasARowOfItsOwn(entityType))
            {
                continue;
            }

            // Through the metadata API rather than modelBuilder.Entity<T>().Property<long>(),
            // because owned types cannot be configured except through their owner and this
            // deliberately does not care which kind of type it is looking at.
            var stamp = entityType.AddProperty(PropertyName, typeof(long));

            stamp.IsNullable = false;
            stamp.SetDefaultValueSql(CurrentTransactionSql);

            // The store owns the value in both directions: EF leaves the column out of its
            // INSERTs and UPDATEs and reads back what the trigger put there. Without this EF
            // would write a zero over it on every save.
            stamp.ValueGenerated = ValueGenerated.OnAddOrUpdate;
        }
    }

    private static bool HasARowOfItsOwn(IMutableEntityType entityType)
    {
        var table = entityType.GetTableName();

        if (table is null)
        {
            return false;
        }

        // An owned type mapped into its owner's table is not a row, it is a few more columns on
        // one — and adding the stamp again there would be a second column with the same name.
        // Ours all have tables of their own; this is here so that stops being load-bearing.
        return entityType.FindOwnership()?.PrincipalEntityType.GetTableName() != table;
    }
}
