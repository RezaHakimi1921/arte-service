using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <summary>
    /// Customer SMS is on by default now (new businesses get it from the model default). This switches it on once for
    /// the businesses that already exist; afterwards an owner who turns it off is not overridden.
    /// Data-only: the schema and the model snapshot do not change.
    /// </summary>
    [DbContext(typeof(ArteDbContext))]
    [Migration("20261031160000_CustomerSmsOnByDefault")]
    public partial class CustomerSmsOnByDefault : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""Tenants"" SET ""CustomerSmsEnabled"" = TRUE, ""SmsOnOpened"" = TRUE, ""SmsOnReady"" = TRUE, ""SmsOnDelivered"" = TRUE;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
