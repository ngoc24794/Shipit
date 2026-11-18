using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shipit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "PipelineId",
                table: "Runs",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "JobId",
                table: "Runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaskPosition",
                table: "RunLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobAllowEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Namespace = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobAllowEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    GitUrl = table.Column<string>(type: "text", nullable: false),
                    GitBranch = table.Column<string>(type: "text", nullable: false, defaultValue: "main"),
                    GitCredentialId = table.Column<long>(type: "bigint", nullable: false),
                    SlaveId = table.Column<long>(type: "bigint", nullable: false),
                    DockerfilePath = table.Column<string>(type: "text", nullable: false, defaultValue: "Dockerfile"),
                    BuildContext = table.Column<string>(type: "text", nullable: true),
                    ImageName = table.Column<string>(type: "text", nullable: false),
                    RegistryHost = table.Column<string>(type: "text", nullable: false),
                    DeployRegistryHost = table.Column<string>(type: "text", nullable: true),
                    RegistryCredentialId = table.Column<long>(type: "bigint", nullable: true),
                    Namespace = table.Column<string>(type: "text", nullable: false),
                    ContainerName = table.Column<string>(type: "text", nullable: false, defaultValue: "app"),
                    ServiceAccount = table.Column<string>(type: "text", nullable: true),
                    ImagePullSecretsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Jobs_Credentials_GitCredentialId",
                        column: x => x.GitCredentialId,
                        principalTable: "Credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Jobs_Credentials_RegistryCredentialId",
                        column: x => x.RegistryCredentialId,
                        principalTable: "Credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Jobs_Slaves_SlaveId",
                        column: x => x.SlaveId,
                        principalTable: "Slaves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunTasks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    K8sJobName = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunTasks_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobTasks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    ArgsJson = table.Column<string>(type: "text", nullable: false),
                    EnvFromConfigMapsJson = table.Column<string>(type: "text", nullable: false),
                    EnvFromSecretsJson = table.Column<string>(type: "text", nullable: false),
                    EnvJson = table.Column<string>(type: "text", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    BackoffLimit = table.Column<int>(type: "integer", nullable: false),
                    TtlSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobTasks_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_JobId_Active",
                table: "Runs",
                column: "JobId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Runs_PipelineXorJob",
                table: "Runs",
                sql: "(\"PipelineId\" IS NOT NULL AND \"JobId\" IS NULL) OR (\"PipelineId\" IS NULL AND \"JobId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_JobAllowEntries_Namespace_Kind_Name",
                table: "JobAllowEntries",
                columns: new[] { "Namespace", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobTasks_JobId_Position",
                table: "JobTasks",
                columns: new[] { "JobId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_GitCredentialId",
                table: "Jobs",
                column: "GitCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Name",
                table: "Jobs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_RegistryCredentialId",
                table: "Jobs",
                column: "RegistryCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_SlaveId",
                table: "Jobs",
                column: "SlaveId");

            migrationBuilder.CreateIndex(
                name: "IX_RunTasks_RunId_Position",
                table: "RunTasks",
                columns: new[] { "RunId", "Position" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_Jobs_JobId",
                table: "Runs",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Runs_Jobs_JobId",
                table: "Runs");

            migrationBuilder.DropTable(
                name: "JobAllowEntries");

            migrationBuilder.DropTable(
                name: "JobTasks");

            migrationBuilder.DropTable(
                name: "RunTasks");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Runs_JobId_Active",
                table: "Runs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Runs_PipelineXorJob",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "TaskPosition",
                table: "RunLogs");

            migrationBuilder.AlterColumn<long>(
                name: "PipelineId",
                table: "Runs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
