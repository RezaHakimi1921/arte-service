using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ArteDbContext))]
    [Migration("20261107120000_SmsWallet")]
    public partial class SmsWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmsSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    PurchasePricePerPartRial = table.Column<long>(type: "bigint", nullable: false),
                    SalePricePerPartRial = table.Column<long>(type: "bigint", nullable: false),
                    CentralBalanceParts = table.Column<long>(type: "bigint", nullable: false),
                    LowCentralParts = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmsWallets",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BalanceParts = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsWallets", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_SmsWallets_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SmsLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Parts = table.Column<long>(type: "bigint", nullable: false),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: true),
                    CentralAfter = table.Column<long>(type: "bigint", nullable: false),
                    AmountRial = table.Column<long>(type: "bigint", nullable: true),
                    MessageKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsLedger_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SmsTemplates",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DraftText = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ApprovedText = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsTemplates", x => new { x.TenantId, x.MessageKey });
                    table.ForeignKey(
                        name: "FK_SmsTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SmsSettings",
                columns: new[] { "Id", "CentralBalanceParts", "LowCentralParts", "PurchasePricePerPartRial", "SalePricePerPartRial", "UpdatedAt" },
                values: new object[] { 1, 0L, 50L, 1000L, 2190L, new DateTimeOffset(new DateTime(2026, 11, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "IX_SmsLedger_TenantId_CreatedAt",
                table: "SmsLedger",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SmsLedger");
            migrationBuilder.DropTable(name: "SmsSettings");
            migrationBuilder.DropTable(name: "SmsTemplates");
            migrationBuilder.DropTable(name: "SmsWallets");
        }
    }
}
