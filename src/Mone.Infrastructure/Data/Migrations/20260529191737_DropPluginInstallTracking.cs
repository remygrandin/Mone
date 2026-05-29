using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropPluginInstallTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstalledAt",
                table: "plugin_manifests");

            migrationBuilder.DropColumn(
                name: "InstalledPath",
                table: "plugin_manifests");

            migrationBuilder.DropColumn(
                name: "IsInstalled",
                table: "plugin_manifests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InstalledAt",
                table: "plugin_manifests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstalledPath",
                table: "plugin_manifests",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInstalled",
                table: "plugin_manifests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
