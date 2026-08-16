using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDispatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserLifecycle : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// The default is <c>true</c>, hand-corrected from the <c>false</c> the generator writes for
        /// a non-nullable bool. Backfilling <c>false</c> would lock every existing user out of a
        /// shop's system the moment the migration ran, which is the sort of thing that is only
        /// discovered by the people it happens to.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "users");
        }
    }
}
