using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Users : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    change_seq = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "pg_current_xact_id()::text::bigint")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_org_id",
                table: "users",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);

            // Every table carries the change stamp; the trigger that keeps it current is the half
            // the model sweep cannot express. The function was created by the SyncOpLog migration,
            // so a new table only has to be pointed at it. SchemaTests fails if this is forgotten.
            //
            // Nothing syncs users — no device asks what changed about a login — but the rule is
            // "every table", with no list of exceptions to keep current, which is what makes it
            // hold for the table somebody adds next.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER stamp_change_seq
                BEFORE INSERT OR UPDATE ON users
                FOR EACH ROW EXECUTE FUNCTION stamp_change_seq();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
