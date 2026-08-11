using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncRemovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_removals",
                columns: table => new
                {
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "pg_current_xact_id()::text::bigint")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_removals", x => x.entity_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_removals_org_id",
                table: "sync_removals",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_removals_technician_id",
                table: "sync_removals",
                column: "technician_id");

            // Every table carries the change stamp, and the trigger that keeps it current is the
            // half EF cannot express — the column arrives from the model sweep, this does not. The
            // function itself was created by the SyncOpLog migration; a new table only needs to be
            // pointed at it. SchemaTests fails if this is forgotten.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER stamp_change_seq
                BEFORE INSERT OR UPDATE ON sync_removals
                FOR EACH ROW EXECUTE FUNCTION stamp_change_seq();
                """);

            // What pull asks of this table: one technician's removals since a cursor. The stamp is
            // a shadow property, so an index over it cannot be declared on the model — the same
            // reason the dispatch board's composite index is hand-written in step 27's migration.
            migrationBuilder.CreateIndex(
                name: "ix_sync_removals_technician_id_change_seq",
                table: "sync_removals",
                columns: ["technician_id", "change_seq"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger goes with the table; the function stays, because the SyncOpLog migration
            // created it and every other table still uses it.
            migrationBuilder.DropTable(
                name: "sync_removals");
        }
    }
}
