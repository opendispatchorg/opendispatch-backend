using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncOpLog : Migration
    {
        // Every table in the model, because every row a tenant owns carries the stamp. The list
        // is here rather than derived because a migration is a record of what was done to a
        // database on a particular day: a loop over today's model would change meaning the next
        // time the model does, and would then no longer describe the database it built.
        private static readonly string[] StampedTables =
        [
            "assignments",
            "customers",
            "invoices",
            "jobs",
            "line_items",
            "organizations",
            "service_locations",
            "sync_ops",
            "technicians",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "technicians",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "service_locations",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "organizations",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "line_items",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "jobs",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "invoices",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "customers",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.AddColumn<long>(
                name: "change_seq",
                table: "assignments",
                type: "bigint",
                nullable: false,
                defaultValueSql: "pg_current_xact_id()::text::bigint");

            migrationBuilder.CreateTable(
                name: "sync_ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    base_version = table.Column<long>(type: "bigint", nullable: false),
                    client_ts = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "pg_current_xact_id()::text::bigint")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_ops", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_ops_org_id",
                table: "sync_ops",
                column: "org_id");

            // The half of the change stamp that EF cannot express. The column default above
            // covers an insert; an update has no default, and a job whose status moved is
            // exactly the change a device is waiting to hear about.
            //
            // It is a trigger rather than an interceptor because the value has to be the writing
            // transaction's own id, taken inside that transaction. A number allocated just before
            // the rows are committed can be handed out as a cursor while those rows are still
            // invisible, and they would then never be sent to that device again. A BEFORE trigger
            // runs inside the writing statement whoever wrote it — this application, the seeder,
            // a migration, or somebody at a psql prompt.
            //
            // Returning the row is safe: Postgres evaluates RETURNING after BEFORE triggers, so
            // EF reads back the stamped value rather than what it did not send.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION stamp_change_seq() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.change_seq := pg_current_xact_id()::text::bigint;
                    RETURN NEW;
                END;
                $$;
                """);

            foreach (var table in StampedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    CREATE TRIGGER stamp_change_seq
                    BEFORE INSERT OR UPDATE ON {table}
                    FOR EACH ROW EXECUTE FUNCTION stamp_change_seq();
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Before the columns they write to, and before sync_ops is dropped: dropping a table
            // takes its trigger with it, dropping a column does not.
            foreach (var table in StampedTables)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS stamp_change_seq ON {table};");
            }

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS stamp_change_seq();");

            migrationBuilder.DropTable(
                name: "sync_ops");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "technicians");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "service_locations");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "line_items");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "change_seq",
                table: "assignments");
        }
    }
}
