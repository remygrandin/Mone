using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProbeAssignmentNamingAndMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_probe_assignments_GroupId",
                table: "probe_assignments");

            migrationBuilder.DropIndex(
                name: "IX_probe_assignments_HostId",
                table: "probe_assignments");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "probe_assignments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameSnakeCase",
                table: "probe_assignments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // Backfill any pre-existing rows with a distinct deterministic placeholder so the
            // partial unique indexes below do not collide. Operators should rename via the UI.
            migrationBuilder.Sql("""
                UPDATE probe_assignments
                SET "Name" = 'probe_' || REPLACE(LOWER("ProbePluginId"), '.', '_') || '_' || SUBSTRING("Id"::text, 1, 8),
                    "NameSnakeCase" = REGEXP_REPLACE(LOWER('probe_' || REPLACE("ProbePluginId", '.', '_') || '_' || SUBSTRING("Id"::text, 1, 8)), '[^a-z0-9_]+', '_', 'g')
                WHERE "Name" = '';
            """);

            migrationBuilder.CreateTable(
                name: "probe_assignment_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProbeAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullKey = table.Column<string>(type: "character varying(384)", maxLength: 384, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ValueMappingJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_probe_assignment_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_probe_assignment_metrics_probe_assignments_ProbeAssignmentId",
                        column: x => x.ProbeAssignmentId,
                        principalTable: "probe_assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignments_GroupId_NameSnakeCase",
                table: "probe_assignments",
                columns: new[] { "GroupId", "NameSnakeCase" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignments_HostId_NameSnakeCase",
                table: "probe_assignments",
                columns: new[] { "HostId", "NameSnakeCase" },
                unique: true,
                filter: "\"HostId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignment_metrics_ProbeAssignmentId_FullKey",
                table: "probe_assignment_metrics",
                columns: new[] { "ProbeAssignmentId", "FullKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "probe_assignment_metrics");

            migrationBuilder.DropIndex(
                name: "IX_probe_assignments_GroupId_NameSnakeCase",
                table: "probe_assignments");

            migrationBuilder.DropIndex(
                name: "IX_probe_assignments_HostId_NameSnakeCase",
                table: "probe_assignments");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "probe_assignments");

            migrationBuilder.DropColumn(
                name: "NameSnakeCase",
                table: "probe_assignments");

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignments_GroupId",
                table: "probe_assignments",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignments_HostId",
                table: "probe_assignments",
                column: "HostId");
        }
    }
}
