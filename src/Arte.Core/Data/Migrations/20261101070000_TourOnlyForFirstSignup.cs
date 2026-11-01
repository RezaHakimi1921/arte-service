using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <summary>
    /// The intro tour opens by itself only at a person's first sign-up. Memberships that never finished it
    /// (added as staff, extra branches) are marked done; data-only, no schema change.
    /// </summary>
    [DbContext(typeof(ArteDbContext))]
    [Migration("20261101070000_TourOnlyForFirstSignup")]
    public partial class TourOnlyForFirstSignup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""Memberships"" SET ""TourDoneAt"" = now() WHERE ""TourDoneAt"" IS NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
