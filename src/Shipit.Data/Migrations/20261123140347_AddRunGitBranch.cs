using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunGitBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GitBranch",
                table: "Runs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitBranch",
                table: "Runs");
        }
    }
}
