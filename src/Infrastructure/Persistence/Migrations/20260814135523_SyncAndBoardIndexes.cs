using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncAndBoardIndexes : Migration
    {
        // The indexes the two read paths that run constantly have always needed and never had.
        //
        // Every one of these queries was written against tables with nothing in them, so all of
        // them were sequential scans that nobody could feel — and all of them are over tables that
        // grow forever. A fleet of phones pulling every minute and a board re-rendering all day is
        // the load this system is for; a shop with a year of history is the point at which the
        // absence of these stops being invisible.
        //
        // Hand-written, like ix_jobs_org_id_status_window_start in the initial migration, and for a
        // related reason: change_seq is a *shadow* property added by ChangeStamps.Apply after the
        // per-aggregate configurations have run, so there is no configuration in which
        // HasIndex could name it. Being outside the model is stable rather than fragile — the
        // snapshot never knew about them, so a later `migrations add` will not try to drop them —
        // and SchemaTests is what notices if one goes missing.
        //
        // NOT every change_seq column. The stamp is on every table because carrying it costs
        // nothing; an index costs a write on every insert and update, so only the tables something
        // actually reads *by* stamp get one. Today that is assignments and jobs.
        private static readonly (string Name, string Table, string Columns)[] Indexes =
        [
            // The pull's own stops: "this technician's assignments, changed since". One index
            // answers both halves, which is why the technician comes first.
            ("ix_assignments_technician_id_change_seq", "assignments", "technician_id, change_seq"),

            // The pull's other half — stops that moved *away* from this technician — is
            // `technician_id <> @me`, which cannot use the index above.
            ("ix_assignments_change_seq", "assignments", "change_seq"),

            // The job side of the same window, and the ceiling probe that decides where a page
            // stops.
            ("ix_jobs_change_seq", "jobs", "change_seq"),

            // The dispatch board, the emergency-insert horizon, and the export's ordering: every
            // one of them reads assignments by scheduled_start, always inside one tenant.
            ("ix_assignments_org_id_scheduled_start", "assignments", "org_id, scheduled_start"),

            // The `prune` verb, which otherwise scans the largest table in the database to delete
            // the oldest tenth of it.
            ("ix_sync_ops_applied_at", "sync_ops", "applied_at"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS, because a deployment with real volume should not take a write lock on
            // its busiest tables to get these: it creates them CONCURRENTLY by hand first (which
            // cannot run inside a migration's transaction) and then this migration finds them
            // already there and does nothing. On a small or empty database the lock is
            // instantaneous and this is the whole procedure.
            foreach (var (name, table, columns) in Indexes)
            {
                migrationBuilder.Sql($"CREATE INDEX IF NOT EXISTS {name} ON {table} ({columns});");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (name, _, _) in Indexes)
            {
                migrationBuilder.Sql($"DROP INDEX IF EXISTS {name};");
            }
        }
    }
}
