using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDamageShopLossReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnMarkedAt",
                table: "Cases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "Cases",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompensationExcessMode",
                table: "CaseItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerDamageId",
                table: "CaseItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DamageReportId",
                table: "CaseAttachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerDamageReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountRials = table.Column<long>(type: "bigint", nullable: false),
                    InternalNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ShowReportToCustomer = table.Column<bool>(type: "boolean", nullable: false),
                    ShowMediaToCustomer = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDamageReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDamageReports_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShopLossReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    AmountRials = table.Column<long>(type: "bigint", nullable: false),
                    InternalNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResponsibleUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopLossReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopLossReports_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShopLossReports_Memberships_ResponsibleUserId",
                        column: x => x.ResponsibleUserId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseItems_CustomerDamageId",
                table: "CaseItems",
                column: "CustomerDamageId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAttachments_DamageReportId",
                table: "CaseAttachments",
                column: "DamageReportId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseAttachments_TenantId_DamageReportId",
                table: "CaseAttachments",
                columns: new[] { "TenantId", "DamageReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDamageReports_CaseId",
                table: "CustomerDamageReports",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDamageReports_TenantId_CaseId",
                table: "CustomerDamageReports",
                columns: new[] { "TenantId", "CaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopLossReports_CaseId",
                table: "ShopLossReports",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopLossReports_ResponsibleUserId",
                table: "ShopLossReports",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopLossReports_TenantId_CaseId",
                table: "ShopLossReports",
                columns: new[] { "TenantId", "CaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopLossReports_TenantId_CreatedAt",
                table: "ShopLossReports",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_CaseAttachments_CustomerDamageReports_DamageReportId",
                table: "CaseAttachments",
                column: "DamageReportId",
                principalTable: "CustomerDamageReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CaseItems_CustomerDamageReports_CustomerDamageId",
                table: "CaseItems",
                column: "CustomerDamageId",
                principalTable: "CustomerDamageReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseAttachments_CustomerDamageReports_DamageReportId",
                table: "CaseAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_CaseItems_CustomerDamageReports_CustomerDamageId",
                table: "CaseItems");

            migrationBuilder.DropTable(
                name: "CustomerDamageReports");

            migrationBuilder.DropTable(
                name: "ShopLossReports");

            migrationBuilder.DropIndex(
                name: "IX_CaseItems_CustomerDamageId",
                table: "CaseItems");

            migrationBuilder.DropIndex(
                name: "IX_CaseAttachments_DamageReportId",
                table: "CaseAttachments");

            migrationBuilder.DropIndex(
                name: "IX_CaseAttachments_TenantId_DamageReportId",
                table: "CaseAttachments");

            migrationBuilder.DropColumn(
                name: "ReturnMarkedAt",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "CompensationExcessMode",
                table: "CaseItems");

            migrationBuilder.DropColumn(
                name: "CustomerDamageId",
                table: "CaseItems");

            migrationBuilder.DropColumn(
                name: "DamageReportId",
                table: "CaseAttachments");
        }
    }
}
