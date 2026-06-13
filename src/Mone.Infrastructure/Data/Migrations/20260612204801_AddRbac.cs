using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mone_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mone_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mone_role_permissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mone_role_permissions", x => new { x.RoleId, x.Resource });
                    table.ForeignKey(
                        name: "FK_mone_role_permissions_mone_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "mone_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mone_user_role_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mone_user_role_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mone_user_role_assignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mone_user_role_assignments_mone_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "mone_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mone_roles_Name",
                table: "mone_roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mone_user_role_assignments_RoleId",
                table: "mone_user_role_assignments",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_mone_user_role_assignments_UserId_RoleId_ScopeType_ScopeId",
                table: "mone_user_role_assignments",
                columns: new[] { "UserId", "RoleId", "ScopeType", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mone_role_permissions");

            migrationBuilder.DropTable(
                name: "mone_user_role_assignments");

            migrationBuilder.DropTable(
                name: "mone_roles");
        }
    }
}
