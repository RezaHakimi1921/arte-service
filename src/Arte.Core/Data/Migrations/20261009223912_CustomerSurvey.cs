using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerSurvey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SurveyDelayMinutes",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "SurveyEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SurveySendOn",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SurveyNotify",
                table: "Memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsAlert = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SurveyInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsibleIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Score = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    IsLow = table.Column<bool>(type: "boolean", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FollowedUpAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FollowedUpBy = table.Column<Guid>(type: "uuid", nullable: true),
                    FollowUpNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyInvites_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SurveyInvites_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SurveyQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyQuestions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NotificationRecipients",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRecipients", x => new { x.NotificationId, x.MembershipId });
                    table.ForeignKey(
                        name: "FK_NotificationRecipients_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationRecipients_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SurveyAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyAnswers_SurveyInvites_InviteId",
                        column: x => x.InviteId,
                        principalTable: "SurveyInvites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SurveyAnswers_SurveyQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "SurveyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SurveyQuestions",
                columns: new[] { "Id", "CreatedAt", "IsActive", "SortOrder", "TenantId", "Text", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("0199c9a0-0000-7000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 1, null, "از کیفیت کاری که روی وسیله‌تان انجام شد چقدر راضی هستید؟", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0199c9a0-0000-7000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 2, null, "از برخورد و پاسخ‌گویی کارکنان چقدر راضی هستید؟", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0199c9a0-0000-7000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 3, null, "از زمان تحویل و شفافیت هزینه‌ها چقدر راضی هستید؟", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecipients_MembershipId_ReadAt",
                table: "NotificationRecipients",
                columns: new[] { "MembershipId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_CreatedAt",
                table: "Notifications",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SurveyAnswers_InviteId_QuestionId",
                table: "SurveyAnswers",
                columns: new[] { "InviteId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SurveyAnswers_QuestionId",
                table: "SurveyAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyInvites_CaseId",
                table: "SurveyInvites",
                column: "CaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SurveyInvites_DueAt",
                table: "SurveyInvites",
                column: "DueAt",
                filter: "\"SentAt\" IS NULL AND \"CancelledAt\" IS NULL AND \"AnsweredAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyInvites_TenantId_AnsweredAt",
                table: "SurveyInvites",
                columns: new[] { "TenantId", "AnsweredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SurveyQuestions_TenantId_SortOrder",
                table: "SurveyQuestions",
                columns: new[] { "TenantId", "SortOrder" });

            // Owners and supervisors follow customer feedback from the start; technicians when the manager chooses.
            migrationBuilder.Sql("UPDATE \"Memberships\" SET \"SurveyNotify\" = true WHERE \"Role\" IN ('owner', 'supervisor');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationRecipients");

            migrationBuilder.DropTable(
                name: "SurveyAnswers");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "SurveyInvites");

            migrationBuilder.DropTable(
                name: "SurveyQuestions");

            migrationBuilder.DropColumn(
                name: "SurveyDelayMinutes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SurveyEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SurveySendOn",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SurveyNotify",
                table: "Memberships");
        }
    }
}
