using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checker_assignment_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckerAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigJsonOverride = table.Column<string>(type: "text", nullable: true),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checker_assignment_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_checker_assignment_overrides_checker_assignments_CheckerAss~",
                        column: x => x.CheckerAssignmentId,
                        principalTable: "checker_assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_checker_assignment_overrides_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "probe_assignment_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProbeAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigJsonOverride = table.Column<string>(type: "text", nullable: true),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_probe_assignment_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_probe_assignment_overrides_hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_probe_assignment_overrides_probe_assignments_ProbeAssignmen~",
                        column: x => x.ProbeAssignmentId,
                        principalTable: "probe_assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignment_overrides_CheckerAssignmentId",
                table: "checker_assignment_overrides",
                column: "CheckerAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignment_overrides_HostId_CheckerAssignmentId",
                table: "checker_assignment_overrides",
                columns: new[] { "HostId", "CheckerAssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignment_overrides_HostId_ProbeAssignmentId",
                table: "probe_assignment_overrides",
                columns: new[] { "HostId", "ProbeAssignmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignment_overrides_ProbeAssignmentId",
                table: "probe_assignment_overrides",
                column: "ProbeAssignmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checker_assignment_overrides");

            migrationBuilder.DropTable(
                name: "probe_assignment_overrides");
        }
    }
}
