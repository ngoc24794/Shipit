using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunsCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Runs_CreatedAt_Id",
                table: "Runs",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Runs_CreatedAt_Id",
                table: "Runs");
        }
    }
}
