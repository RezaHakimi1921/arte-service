using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireTransferReceipt",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "CaseAttachments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "photo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireTransferReceipt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ReceiptId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "CaseAttachments");
        }
    }
}
