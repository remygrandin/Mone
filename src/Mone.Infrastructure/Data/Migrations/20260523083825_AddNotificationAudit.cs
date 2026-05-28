using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckerId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NotificationPluginId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeliveryStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    PreviousMonitoringStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentMonitoringStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_audit", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_audit_TargetId",
                table: "notification_audit",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_audit_Timestamp",
                table: "notification_audit",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_audit");
        }
    }
}
