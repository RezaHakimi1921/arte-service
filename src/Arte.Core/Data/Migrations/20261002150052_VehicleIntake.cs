using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class VehicleIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing businesses: the workflow name no longer mentions motorcycles.
            migrationBuilder.Sql("UPDATE \"Workflows\" SET \"Name\" = 'پذیرش و تعمیر وسیله نقلیه' WHERE \"SourceTemplateKey\" = 'motorcycle_repair' AND \"Name\" = 'تعمیر موتورسیکلت';");

            migrationBuilder.AddColumn<string>(
                name: "BodyNotes",
                table: "Cases",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BodyStatus",
                table: "Cases",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "FuelLevel",
                table: "Cases",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "RequestedServices",
                table: "Cases",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyNotes",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "BodyStatus",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "FuelLevel",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "RequestedServices",
                table: "Cases");
        }
    }
}
