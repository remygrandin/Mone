using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultPluginRepository : Migration
    {
        private static readonly Guid DefaultRepoId = new("a0c1f7a2-7e0d-4f7e-9b6d-9d2f0b9f1a01");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "plugin_repositories",
                columns: new[] { "Id", "Owner", "Repo", "Branch", "DisplayName", "Enabled", "ETag", "LastSyncedAt", "LastSyncError", "CreatedAt" },
                values: new object[]
                {
                    DefaultRepoId,
                    "remygrandin",
                    "Mone-Plugins",
                    "main",
                    "Mone Official Plugins",
                    true,
                    null,
                    null,
                    null,
                    new DateTime(2026, 5, 29, 0, 0, 0, DateTimeKind.Utc)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "plugin_repositories",
                keyColumn: "Id",
                keyValue: DefaultRepoId);
        }
    }
}
