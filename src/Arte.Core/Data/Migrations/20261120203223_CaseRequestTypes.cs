using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CaseRequestTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "RequestTypes",
                table: "Cases",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            // Pre-model cases: classify as «سایر» without touching problems/services/checklist/events.
            migrationBuilder.Sql("""
                UPDATE "Cases"
                SET "RequestTypes" = ARRAY['other']::text[]
                WHERE cardinality("RequestTypes") = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestTypes",
                table: "Cases");
        }
    }
}
