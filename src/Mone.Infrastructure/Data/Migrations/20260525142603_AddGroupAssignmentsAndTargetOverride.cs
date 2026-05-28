using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupAssignmentsAndTargetOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_checker_assignments_hosts_HostId",
                table: "checker_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_probe_assignments_hosts_HostId",
                table: "probe_assignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "HostId",
                table: "probe_assignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "probe_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetAddressOverride",
                table: "probe_assignments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "HostId",
                table: "checker_assignments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "checker_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_probe_assignments_GroupId",
                table: "probe_assignments",
                column: "GroupId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_probe_assignments_host_or_group",
                table: "probe_assignments",
                sql: "(\"HostId\" IS NOT NULL AND \"GroupId\" IS NULL) OR (\"HostId\" IS NULL AND \"GroupId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_checker_assignments_GroupId",
                table: "checker_assignments",
                column: "GroupId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_checker_assignments_host_or_group",
                table: "checker_assignments",
                sql: "(\"HostId\" IS NOT NULL AND \"GroupId\" IS NULL) OR (\"HostId\" IS NULL AND \"GroupId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_checker_assignments_host_groups_GroupId",
                table: "checker_assignments",
                column: "GroupId",
                principalTable: "host_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_checker_assignments_hosts_HostId",
                table: "checker_assignments",
                column: "HostId",
                principalTable: "hosts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_probe_assignments_host_groups_GroupId",
                table: "probe_assignments",
                column: "GroupId",
                principalTable: "host_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_probe_assignments_hosts_HostId",
                table: "probe_assignments",
                column: "HostId",
                principalTable: "hosts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_checker_assignments_host_groups_GroupId",
                table: "checker_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_checker_assignments_hosts_HostId",
                table: "checker_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_probe_assignments_host_groups_GroupId",
                table: "probe_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_probe_assignments_hosts_HostId",
                table: "probe_assignments");

            migrationBuilder.DropIndex(
                name: "IX_probe_assignments_GroupId",
                table: "probe_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_probe_assignments_host_or_group",
                table: "probe_assignments");

            migrationBuilder.DropIndex(
                name: "IX_checker_assignments_GroupId",
                table: "checker_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_checker_assignments_host_or_group",
                table: "checker_assignments");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "probe_assignments");

            migrationBuilder.DropColumn(
                name: "TargetAddressOverride",
                table: "probe_assignments");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "checker_assignments");

            migrationBuilder.AlterColumn<Guid>(
                name: "HostId",
                table: "probe_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "HostId",
                table: "checker_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_checker_assignments_hosts_HostId",
                table: "checker_assignments",
                column: "HostId",
                principalTable: "hosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_probe_assignments_hosts_HostId",
                table: "probe_assignments",
                column: "HostId",
                principalTable: "hosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
