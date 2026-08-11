using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Attachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "pg_current_xact_id()::text::bigint"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_job_id",
                table: "attachments",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_org_id",
                table: "attachments",
                column: "org_id");

            // Every table carries the change stamp; the trigger that keeps it current is the half
            // the model sweep cannot express. The function was created by the SyncOpLog migration,
            // so a new table only has to be pointed at it. SchemaTests fails if this is forgotten.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER stamp_change_seq
                BEFORE INSERT OR UPDATE ON attachments
                FOR EACH ROW EXECUTE FUNCTION stamp_change_seq();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments");
        }
    }
}
