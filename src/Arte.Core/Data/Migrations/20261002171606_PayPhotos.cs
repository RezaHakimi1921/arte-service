using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class PayPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommissionBase",
                table: "Memberships",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "case_total");

            migrationBuilder.AddColumn<long>(
                name: "CommissionFixedRials",
                table: "Memberships",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommissionType",
                table: "Memberships",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.CreateTable(
                name: "CaseAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Caption = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseAttachments_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseAttachments_CaseId",
                table: "CaseAttachments",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAttachments_TenantId_CaseId",
                table: "CaseAttachments",
                columns: new[] { "TenantId", "CaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseAttachments");

            migrationBuilder.DropColumn(
                name: "CommissionBase",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CommissionFixedRials",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CommissionType",
                table: "Memberships");
        }
    }
}
