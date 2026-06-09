using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckerAssignmentNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_checker_assignments_GroupId",
                table: "checker_assignments");

            migrationBuilder.DropIndex(
                name: "IX_checker_assignments_HostId",
                table: "checker_assignments");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "checker_assignments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameSnakeCase",
                table: "checker_assignments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // Backfill any pre-existing rows with a distinct deterministic placeholder so the
            // partial unique indexes below do not collide. Operators should rename via the UI.
            migrationBuilder.Sql("""
                UPDATE checker_assignments
                SET "Name" = 'checker_' || REPLACE(LOWER("CheckerPluginId"), '.', '_') || '_' || SUBSTRING("Id"::text, 1, 8),
                    "NameSnakeCase" = REGEXP_REPLACE(LOWER('checker_' || REPLACE("CheckerPluginId", '.', '_') || '_' || SUBSTRING("Id"::text, 1, 8)), '[^a-z0-9_]+', '_', 'g')
                WHERE "Name" = '';
            """);

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignments_GroupId_NameSnakeCase",
                table: "checker_assignments",
                columns: new[] { "GroupId", "NameSnakeCase" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignments_HostId_NameSnakeCase",
                table: "checker_assignments",
                columns: new[] { "HostId", "NameSnakeCase" },
                unique: true,
                filter: "\"HostId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_checker_assignments_GroupId_NameSnakeCase",
                table: "checker_assignments");

            migrationBuilder.DropIndex(
                name: "IX_checker_assignments_HostId_NameSnakeCase",
                table: "checker_assignments");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "checker_assignments");

            migrationBuilder.DropColumn(
                name: "NameSnakeCase",
                table: "checker_assignments");

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignments_GroupId",
                table: "checker_assignments",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignments_HostId",
                table: "checker_assignments",
                column: "HostId");
        }
    }
}
