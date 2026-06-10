using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutorNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExecutorNodeId",
                table: "probe_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutorNodeId",
                table: "checker_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "executor_nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executor_nodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignments_ExecutorNodeId",
                table: "probe_assignments",
                column: "ExecutorNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignments_ExecutorNodeId",
                table: "checker_assignments",
                column: "ExecutorNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_executor_nodes_Name",
                table: "executor_nodes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_checker_assignments_executor_nodes_ExecutorNodeId",
                table: "checker_assignments",
                column: "ExecutorNodeId",
                principalTable: "executor_nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_probe_assignments_executor_nodes_ExecutorNodeId",
                table: "probe_assignments",
                column: "ExecutorNodeId",
                principalTable: "executor_nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_checker_assignments_executor_nodes_ExecutorNodeId",
                table: "checker_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_probe_assignments_executor_nodes_ExecutorNodeId",
                table: "probe_assignments");

            migrationBuilder.DropTable(
                name: "executor_nodes");

            migrationBuilder.DropIndex(
                name: "IX_probe_assignments_ExecutorNodeId",
                table: "probe_assignments");

            migrationBuilder.DropIndex(
                name: "IX_checker_assignments_ExecutorNodeId",
                table: "checker_assignments");

            migrationBuilder.DropColumn(
                name: "ExecutorNodeId",
                table: "probe_assignments");

            migrationBuilder.DropColumn(
                name: "ExecutorNodeId",
                table: "checker_assignments");
        }
    }
}
