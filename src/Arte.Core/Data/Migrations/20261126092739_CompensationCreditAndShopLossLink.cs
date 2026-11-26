using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompensationCreditAndShopLossLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CompensationCreditRials",
                table: "CaseItems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "CompensationShopLossId",
                table: "CaseItems",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompensationCreditRials",
                table: "CaseItems");

            migrationBuilder.DropColumn(
                name: "CompensationShopLossId",
                table: "CaseItems");
        }
    }
}
