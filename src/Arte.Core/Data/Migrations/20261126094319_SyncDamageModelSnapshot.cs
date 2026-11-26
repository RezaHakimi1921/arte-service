using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncDamageModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CaseItems_CompensationShopLossId",
                table: "CaseItems",
                column: "CompensationShopLossId");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseItems_ShopLossReports_CompensationShopLossId",
                table: "CaseItems",
                column: "CompensationShopLossId",
                principalTable: "ShopLossReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseItems_ShopLossReports_CompensationShopLossId",
                table: "CaseItems");

            migrationBuilder.DropIndex(
                name: "IX_CaseItems_CompensationShopLossId",
                table: "CaseItems");
        }
    }
}
