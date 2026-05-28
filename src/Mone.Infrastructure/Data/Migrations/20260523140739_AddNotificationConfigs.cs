using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PluginId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_configs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_configs_PluginId",
                table: "notification_configs",
                column: "PluginId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_configs");
        }
    }
}
