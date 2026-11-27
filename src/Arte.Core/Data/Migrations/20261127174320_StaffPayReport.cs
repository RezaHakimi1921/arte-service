using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffPayReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentEndedOn",
                table: "Memberships",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentStartedOn",
                table: "Memberships",
                type: "date",
                nullable: true);

            // Tehran is a fixed +3:30. Existing rows take the calendar day of CreatedAt in that offset.
            migrationBuilder.Sql("""
                UPDATE "Memberships"
                SET "EmploymentStartedOn" = ((("CreatedAt" AT TIME ZONE 'UTC') + INTERVAL '3 hours 30 minutes')::date)
                WHERE "EmploymentStartedOn" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EmploymentStartedOn",
                table: "Memberships",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "CaseCommissionGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseRials = table.Column<long>(type: "bigint", nullable: false),
                    CommissionRials = table.Column<long>(type: "bigint", nullable: false),
                    CommissionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    CommissionFixedRials = table.Column<long>(type: "bigint", nullable: true),
                    CommissionBase = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseCommissionGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseCommissionGrants_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaseCommissionGrants_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffBonuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountRials = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    JalaliYear = table.Column<int>(type: "integer", nullable: false),
                    JalaliMonth = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffBonuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffBonuses_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffDeductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AmountRials = table.Column<long>(type: "bigint", nullable: false),
                    JalaliYear = table.Column<int>(type: "integer", nullable: false),
                    JalaliMonth = table.Column<int>(type: "integer", nullable: false),
                    ShopLossId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffDeductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffDeductions_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffDeductions_ShopLossReports_ShopLossId",
                        column: x => x.ShopLossId,
                        principalTable: "ShopLossReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffPayAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPayAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffPaySettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    JalaliYear = table.Column<int>(type: "integer", nullable: false),
                    JalaliMonth = table.Column<int>(type: "integer", nullable: false),
                    FixedSalaryRials = table.Column<long>(type: "bigint", nullable: false),
                    CommissionRials = table.Column<long>(type: "bigint", nullable: false),
                    BonusRials = table.Column<long>(type: "bigint", nullable: false),
                    DeductionRials = table.Column<long>(type: "bigint", nullable: false),
                    ShopLossRials = table.Column<long>(type: "bigint", nullable: false),
                    IncomeRials = table.Column<long>(type: "bigint", nullable: false),
                    AfterDeductionsRials = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPaySettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffPaySettlements_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseCommissionGrants_CaseId",
                table: "CaseCommissionGrants",
                column: "CaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseCommissionGrants_MembershipId",
                table: "CaseCommissionGrants",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseCommissionGrants_TenantId_MembershipId_GrantedAt",
                table: "CaseCommissionGrants",
                columns: new[] { "TenantId", "MembershipId", "GrantedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffBonuses_MembershipId",
                table: "StaffBonuses",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffBonuses_TenantId_MembershipId_JalaliYear_JalaliMonth",
                table: "StaffBonuses",
                columns: new[] { "TenantId", "MembershipId", "JalaliYear", "JalaliMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffDeductions_MembershipId",
                table: "StaffDeductions",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffDeductions_ShopLossId",
                table: "StaffDeductions",
                column: "ShopLossId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffDeductions_TenantId_MembershipId_JalaliYear_JalaliMonth",
                table: "StaffDeductions",
                columns: new[] { "TenantId", "MembershipId", "JalaliYear", "JalaliMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPayAuditLogs_EntityType_EntityId",
                table: "StaffPayAuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPayAuditLogs_TenantId_At",
                table: "StaffPayAuditLogs",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPaySettlements_MembershipId",
                table: "StaffPaySettlements",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffPaySettlements_TenantId_MembershipId_JalaliYear_Jalali~",
                table: "StaffPaySettlements",
                columns: new[] { "TenantId", "MembershipId", "JalaliYear", "JalaliMonth" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseCommissionGrants");

            migrationBuilder.DropTable(
                name: "StaffBonuses");

            migrationBuilder.DropTable(
                name: "StaffDeductions");

            migrationBuilder.DropTable(
                name: "StaffPayAuditLogs");

            migrationBuilder.DropTable(
                name: "StaffPaySettlements");

            migrationBuilder.DropColumn(
                name: "EmploymentEndedOn",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "EmploymentStartedOn",
                table: "Memberships");
        }
    }
}
