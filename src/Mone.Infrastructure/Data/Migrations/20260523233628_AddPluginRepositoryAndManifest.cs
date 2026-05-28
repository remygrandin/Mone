using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginRepositoryAndManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plugin_repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Repo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Branch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ETag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plugin_manifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PluginType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DownloadUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    DependenciesJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    MinMoneVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Author = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    License = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Homepage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TagsJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsInstalled = table.Column<bool>(type: "boolean", nullable: false),
                    InstalledPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    InstalledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plugin_manifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plugin_manifests_plugin_repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "plugin_repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_plugin_manifests_RepositoryId",
                table: "plugin_manifests",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_plugin_manifests_RepositoryId_Name_Version",
                table: "plugin_manifests",
                columns: new[] { "RepositoryId", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plugin_repositories_Owner_Repo",
                table: "plugin_repositories",
                columns: new[] { "Owner", "Repo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plugin_manifests");

            migrationBuilder.DropTable(
                name: "plugin_repositories");
        }
    }
}
