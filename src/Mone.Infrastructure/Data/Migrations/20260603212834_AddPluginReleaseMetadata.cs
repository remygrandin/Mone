using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginReleaseMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-existing rows have no release metadata; drop them so the next
            // sync rebuilds the catalog cleanly from the per-release manifests.
            migrationBuilder.Sql("DELETE FROM plugin_manifests;");
            migrationBuilder.Sql("UPDATE plugin_repositories SET \"ETag\" = NULL;");

            migrationBuilder.AddColumn<bool>(
                name: "IsPrerelease",
                table: "plugin_manifests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "plugin_manifests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ReleaseTag",
                table: "plugin_manifests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_plugin_manifests_RepositoryId_ReleaseTag",
                table: "plugin_manifests",
                columns: new[] { "RepositoryId", "ReleaseTag" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_plugin_manifests_RepositoryId_ReleaseTag",
                table: "plugin_manifests");

            migrationBuilder.DropColumn(
                name: "IsPrerelease",
                table: "plugin_manifests");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "plugin_manifests");

            migrationBuilder.DropColumn(
                name: "ReleaseTag",
                table: "plugin_manifests");
        }
    }
}
