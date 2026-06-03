using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mone.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProbeResultsMetadataJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE probe_results
                ALTER COLUMN "MetadataJson" TYPE jsonb
                USING CASE
                    WHEN "MetadataJson" IS NULL THEN NULL
                    WHEN "MetadataJson" = '' THEN NULL
                    ELSE "MetadataJson"::jsonb
                END;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "MetadataJson",
                table: "probe_results",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
