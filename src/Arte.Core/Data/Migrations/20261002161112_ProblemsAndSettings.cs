using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProblemsAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireAssigneeOnIntake",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string[]>(
                name: "ReportedProblems",
                table: "Cases",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireAssigneeOnIntake",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ReportedProblems",
                table: "Cases");
        }
    }
}
