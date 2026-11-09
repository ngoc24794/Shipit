using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shipit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeployRegistryHost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeployRegistryHost",
                table: "Pipelines",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeployRegistryHost",
                table: "Pipelines");
        }
    }
}
