using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Outbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "pg_current_xact_id()::text::bigint")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at");

            // Every table carries the change stamp, and the trigger that keeps it current is the
            // half the model sweep cannot express. Nothing syncs the outbox — it is delivery
            // bookkeeping, not a tenant's data — but the rule has no list of exceptions to keep,
            // which is what makes it hold for the table somebody adds next. SchemaTests fails
            // without this.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER stamp_change_seq
                BEFORE INSERT OR UPDATE ON outbox_messages
                FOR EACH ROW EXECUTE FUNCTION stamp_change_seq();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
