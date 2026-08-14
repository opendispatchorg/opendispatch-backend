using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "byte_length",
                table: "attachments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "attachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Rows captured before this migration have no recorded type, and the truth about them is
            // that nobody knows: they were stored when the upload path did not ask. The generic
            // binary type says exactly that, and is what the download will answer with — a browser
            // saves the file rather than being told something untrue about it. New rows cannot land
            // here, because Attachment.Create refuses anything outside its allow-list.
            migrationBuilder.Sql(
                "UPDATE attachments SET content_type = 'application/octet-stream' WHERE content_type = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "byte_length",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "attachments");
        }
    }
}
