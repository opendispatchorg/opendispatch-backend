using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "org_id",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_org_id_occurred_at",
                table: "outbox_messages",
                columns: new[] { "org_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_org_id_occurred_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "org_id",
                table: "outbox_messages");
        }
    }
}
