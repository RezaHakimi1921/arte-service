using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class BusinessTypeAndTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessType",
                table: "Tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "motorcycle_repair");

            migrationBuilder.AddColumn<bool>(
                name: "CustomerSmsEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnDelivered",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnOpened",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnReady",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrackShowAmounts",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrackShowItems",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrackShowStages",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string[]>(
                name: "VehicleKinds",
                table: "Tenants",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{car,suv,van,pickup,motorcycle}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "TrackingCode",
                table: "Cases",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Cases opened before tracking links existed get a code too (hex, 12 chars).
            migrationBuilder.Sql("UPDATE \"Cases\" SET \"TrackingCode\" = substr(md5(random()::text || \"Id\"::text), 1, 12) WHERE \"TrackingCode\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_TrackingCode",
                table: "Cases",
                column: "TrackingCode",
                unique: true,
                filter: "\"TrackingCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cases_TrackingCode",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CustomerSmsEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SmsOnDelivered",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SmsOnOpened",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SmsOnReady",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackShowAmounts",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackShowItems",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackShowStages",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "VehicleKinds",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrackingCode",
                table: "Cases");
        }
    }
}
