using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(ArteDbContext))]
[Migration("20261111160000_SmsTemplateChangeRequests")]
public partial class SmsTemplateChangeRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SmsTemplateChangeRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                MessageKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ProposedText = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReviewNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SmsTemplateChangeRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_SmsTemplateChangeRequests_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SmsTemplateChangeRequests_Status_CreatedAt",
            table: "SmsTemplateChangeRequests",
            columns: new[] { "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_SmsTemplateChangeRequests_TenantId_MessageKey_CreatedAt",
            table: "SmsTemplateChangeRequests",
            columns: new[] { "TenantId", "MessageKey", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SmsTemplateChangeRequests");
    }
}
