using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    username = table.Column<string>(type: "text", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    targets = table.Column<string>(type: "jsonb", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "pg_current_xact_id()::text::bigint")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_org_id_at",
                table: "audit_entries",
                columns: new[] { "org_id", "at" });

            // Every table carries the change stamp; the trigger that keeps it current is the half
            // the model sweep cannot express. SchemaTests fails without it. Nothing syncs the audit
            // trail — but the rule has no list of exceptions to remember, which is what makes it
            // hold for the table somebody adds next.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER stamp_change_seq
                BEFORE INSERT OR UPDATE ON audit_entries
                FOR EACH ROW EXECUTE FUNCTION stamp_change_seq();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");
        }
    }
}
