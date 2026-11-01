using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arte.Core.Data.Migrations
{
    /// <summary>
    /// A person's name now belongs to the branch (Membership), not to the shared user, so renaming someone in one
    /// branch cannot change or expose their name in another. Every existing membership starts with the name it showed before.
    /// </summary>
    [DbContext(typeof(ArteDbContext))]
    [Migration("20261101060000_MembershipDisplayName")]
    public partial class MembershipDisplayName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "DisplayName", table: "Memberships", type: "character varying(80)", maxLength: 80, nullable: true);
            migrationBuilder.Sql(@"UPDATE ""Memberships"" m SET ""DisplayName"" = u.""DisplayName"" FROM ""Users"" u WHERE u.""Id"" = m.""UserId"";");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DisplayName", table: "Memberships");
        }
    }
}
