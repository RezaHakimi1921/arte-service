using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class BusinessPhoneFromOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Businesses without a phone get their owner's mobile (sign-up now does this itself).
            migrationBuilder.Sql(@"UPDATE ""Tenants"" t SET ""Phone"" = u.""Mobile""
                FROM ""Memberships"" m JOIN ""Users"" u ON u.""Id"" = m.""UserId""
                WHERE m.""TenantId"" = t.""Id"" AND m.""Role"" = 'owner' AND NOT m.""IsSupport""
                  AND (t.""Phone"" IS NULL OR t.""Phone"" = '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
