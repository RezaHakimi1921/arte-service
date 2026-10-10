using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Arte.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class StandardCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StandardItemId",
                table: "CatalogItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VehicleKinds = table.Column<string[]>(type: "text[]", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StandardItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VehicleKinds = table.Column<string[]>(type: "text[]", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServicePackageLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    StandardItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Optional = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePackageLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePackageLines_ServicePackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "ServicePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServicePackageLines_StandardItems_StandardItemId",
                        column: x => x.StandardItemId,
                        principalTable: "StandardItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ServicePackages",
                columns: new[] { "Id", "Description", "IsActive", "SortOrder", "Title", "VehicleKinds" },
                values: new object[,]
                {
                    { new Guid("033f95cc-c94f-5c3a-967c-a588a6535a56"), "روغن موتور و اجرت؛ فیلتر روغن اختیاری", true, 1, "تعویض روغن", new[] { "motorcycle" } },
                    { new Guid("2ce1b3fe-fd59-569c-8662-5b866d3f8299"), "روغن، فیلترها و اجرت؛ شمع اختیاری", true, 7, "سرویس دوره‌ای", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("41fe2c73-22b5-5355-aab1-c161a08ddec8"), "تسمه، وزنه‌ها و اجرت (اسکوتر)", true, 5, "سرویس CVT", new[] { "motorcycle" } },
                    { new Guid("48204068-ffca-5cc3-8147-cf572e00b6de"), "روغن، فیلترها، شمع و اجرت سرویس", true, 2, "سرویس دوره‌ای", new[] { "motorcycle" } },
                    { new Guid("65e96321-1f3b-5e8c-b019-7771bc44d927"), "روغن موتور، فیلتر روغن و اجرت", true, 6, "تعویض روغن", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("929594f9-d855-5b9c-8de9-8e359a91053d"), "تسمه، بلبرینگ و اجرت؛ واتر پمپ اختیاری", true, 9, "تعویض تسمه تایم", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("a2c0b851-b614-50e4-bd27-0010ad9afdf4"), "زنجیر، دنده جلو، طبق‌دنده و اجرت", true, 4, "تعویض کیت زنجیر", new[] { "motorcycle" } },
                    { new Guid("d5d9888f-fdac-5261-b8c8-d9153d15915c"), "لنت جلو و اجرت؛ دیسک و روغن ترمز اختیاری", true, 8, "سرویس ترمز", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("e61998ad-e4e0-5c1c-a041-37f2f31daff1"), "گاز کولر و اجرت شارژ", true, 10, "سرویس کولر", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("ef8f6291-db2a-5b00-b372-e168f23b323a"), "لنت و اجرت؛ روغن ترمز اختیاری", true, 3, "سرویس ترمز", new[] { "motorcycle" } }
                });

            migrationBuilder.InsertData(
                table: "StandardItems",
                columns: new[] { "Id", "Category", "IsActive", "Kind", "SortOrder", "Title", "VehicleKinds" },
                values: new object[,]
                {
                    { new Guid("016c200e-08f2-5f0c-bf67-3038cd6e5f4c"), "موتور", true, "part", 58, "بلبرینگ تسمه‌سفت‌کن", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("085128d6-c25b-5885-822b-0e8b9f03d341"), "روغن و فیلتر", true, "part", 3, "فیلتر هوا", new[] { "motorcycle" } },
                    { new Guid("0858f920-6479-5b80-9f69-3c12fba12865"), "اجرت و خدمات", true, "labor", 43, "عیب‌یابی برق", new[] { "motorcycle" } },
                    { new Guid("0ab2936c-8d1d-5a4f-957c-5daa6da1ef72"), "روغن و فیلتر", true, "part", 51, "روغن گیربکس (لیتر)", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("0b48b22e-2979-5cbd-ba9e-b685caa13f34"), "روغن و فیلتر", true, "part", 5, "روغن گیربکس (اسکوتر)", new[] { "motorcycle" } },
                    { new Guid("0e042845-15c7-57f5-ab0b-128df27667c2"), "اجرت و خدمات", true, "labor", 85, "عیب‌یابی و دیاگ", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("10564b98-6bcd-55e3-90e0-68d81f7f8ff4"), "اجرت و خدمات", true, "labor", 81, "تعویض لنت", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("14f6cc4d-9ce3-5bd7-b247-cc9ea700af47"), "اجرت و خدمات", true, "labor", 42, "تعمیر موتور (کامل)", new[] { "motorcycle" } },
                    { new Guid("1583fe4c-28e7-5bbd-b407-2c7452b8e489"), "ترمز", true, "part", 13, "لنت ترمز عقب", new[] { "motorcycle" } },
                    { new Guid("17088331-a704-5dc8-8e45-694851d58797"), "اجرت و خدمات", true, "labor", 37, "تنظیم سوپاپ", new[] { "motorcycle" } },
                    { new Guid("2050aa5b-21e0-5d1b-b6a0-a470ea187132"), "برق", true, "part", 28, "کویل", new[] { "motorcycle" } },
                    { new Guid("230c04b8-8369-5b8f-ac71-9bf0a1b80f7a"), "موتور", true, "part", 57, "تسمه تایم", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("25759973-2e38-5e05-ac65-b93ccebaf014"), "موتور", true, "part", 59, "تسمه دینام", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("25ed3630-78b5-5e80-8b09-1fb5a0eb0e55"), "اجرت و خدمات", true, "labor", 88, "تعویض باتری", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("25ffbb3b-6120-5310-b2b4-eb9fc828827f"), "موتور", true, "part", 56, "وایر شمع", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("2746a228-800e-5a26-89e1-cd2f53312934"), "برق و کولر", true, "part", 74, "باتری", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("2cc83a9f-07f8-5875-ab03-e4ec1ecd929c"), "اجرت و خدمات", true, "labor", 82, "تعویض تسمه تایم", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("2f3b9da6-95a0-52f8-bd2f-ee03bf4c6911"), "موتور", true, "part", 9, "پیستون و رینگ", new[] { "motorcycle" } },
                    { new Guid("3065ba0d-c181-582f-8096-8542d7024503"), "برق", true, "part", 26, "لامپ چراغ جلو", new[] { "motorcycle" } },
                    { new Guid("337c5f35-49fd-573b-b73c-47160323aa89"), "روغن و فیلتر", true, "part", 50, "فیلتر بنزین", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("342044cf-0c78-50b6-abac-82209f37a5de"), "روغن و فیلتر", true, "part", 4, "فیلتر بنزین", new[] { "motorcycle" } },
                    { new Guid("359881f8-df5f-5179-85e6-f2ce8ecc0917"), "چرخ", true, "part", 31, "لاستیک عقب", new[] { "motorcycle" } },
                    { new Guid("366c73db-c5bb-546c-9de9-0be555eb3331"), "جلوبندی و تعلیق", true, "part", 66, "کمک‌فنر جلو", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("38ce647d-75d4-5fa8-ab8d-d523971935a1"), "روغن و فیلتر", true, "part", 47, "فیلتر روغن", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("38de0d47-a736-5ac6-ae5b-72a9e73a754c"), "ترمز", true, "part", 12, "لنت ترمز جلو", new[] { "motorcycle" } },
                    { new Guid("39942727-73cb-5b81-8983-f5f6832a0a9e"), "ترمز", true, "part", 65, "روغن ترمز", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("3b11180f-e4df-5c03-8096-b8cfc0ba7a48"), "روغن و فیلتر", true, "part", 6, "روغن کمک جلو", new[] { "motorcycle" } },
                    { new Guid("3c11f57b-0ec7-53d0-a907-d006029b1327"), "برق و کولر", true, "part", 76, "تیغه برف‌پاک‌کن", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("3c264a3f-9484-51eb-9b9b-3c5b153e3c11"), "اجرت و خدمات", true, "labor", 90, "تعمیر گیربکس", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("3c3e319b-aafd-53fd-be5a-afd469db025a"), "چرخ", true, "part", 32, "تیوب", new[] { "motorcycle" } },
                    { new Guid("3f131158-7423-51a4-8c90-7962fd46881f"), "ترمز", true, "part", 62, "لنت ترمز جلو", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("3ffda0e4-acc3-559b-b41f-cc92861a406a"), "برق", true, "part", 25, "باتری", new[] { "motorcycle" } },
                    { new Guid("4cd01be1-34ac-5dbc-ade3-5b4f6d3f81a2"), "روغن و فیلتر", true, "part", 2, "فیلتر روغن", new[] { "motorcycle" } },
                    { new Guid("4d8533eb-a1e1-5de1-98c1-ba82f575f1fb"), "اجرت و خدمات", true, "labor", 80, "سرویس دوره‌ای", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("4dfbc49c-0c44-5728-abc3-7a716ffb2aab"), "جلوبندی و تعلیق", true, "part", 70, "بوش طبق", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("4e778a2e-be68-52cb-8b11-08a02cde9843"), "جلوبندی و تعلیق", true, "part", 67, "کمک‌فنر عقب", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("517a77af-6ea9-55cb-919d-191e9631ba77"), "اجرت و خدمات", true, "labor", 40, "سرویس CVT", new[] { "motorcycle" } },
                    { new Guid("5444af96-0ffc-5a95-92e3-e0f312af708b"), "روغن و فیلتر", true, "part", 46, "روغن موتور (لیتر)", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("55b7ea30-15cf-536e-ba93-e4fb0e566ea0"), "اجرت و خدمات", true, "labor", 35, "سرویس دوره‌ای", new[] { "motorcycle" } },
                    { new Guid("5802be7a-1239-5da6-9bc7-7bb0d5e33617"), "چرخ", true, "part", 78, "لاستیک", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("583cadb0-b54b-5cde-b6c5-40db77c62dba"), "اجرت و خدمات", true, "labor", 83, "تعمیر کلاچ", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("5e1c1d1b-6f9d-528e-844c-69471563b96e"), "چرخ", true, "part", 30, "لاستیک جلو", new[] { "motorcycle" } },
                    { new Guid("5e28aba4-dadd-5994-a843-97c280563b8f"), "انتقال قدرت", true, "part", 21, "وزنه (رولر) کلاچ", new[] { "motorcycle" } },
                    { new Guid("60279a8d-6dfd-549f-9242-a2f37d279b8d"), "اجرت و خدمات", true, "labor", 92, "تعویض روغن گیربکس", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("605fdba7-132d-5183-858e-a113efd2d63b"), "انتقال قدرت", true, "part", 17, "زنجیر", new[] { "motorcycle" } },
                    { new Guid("60617ee8-587a-5c58-b75d-d2ccf0bf5dcb"), "اجرت و خدمات", true, "labor", 79, "تعویض روغن و فیلتر", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("60b8dba4-f487-5f6e-81c0-8c4f7d714eff"), "اجرت و خدمات", true, "labor", 36, "سرویس کاربراتور", new[] { "motorcycle" } },
                    { new Guid("64877636-601e-5ecb-82cd-614fcf6f6661"), "موتور", true, "part", 11, "کاربراتور", new[] { "motorcycle" } },
                    { new Guid("65beb466-4e8e-56bd-9549-794d5f5f1b9d"), "جلوبندی و تعلیق", true, "part", 71, "بلبرینگ چرخ", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("68df2c35-3a48-511b-816f-3ac96eb34ced"), "انتقال قدرت", true, "part", 24, "سیم گاز", new[] { "motorcycle" } },
                    { new Guid("763db814-1148-5fb2-b116-6f02fb4ca0aa"), "انتقال قدرت", true, "part", 20, "تسمه CVT", new[] { "motorcycle" } },
                    { new Guid("7eb65ddc-e5c9-5b2d-baeb-18a72a609282"), "برق و کولر", true, "part", 77, "گاز کولر", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("80884141-420e-56c2-af60-78ca6328796c"), "اجرت و خدمات", true, "labor", 91, "تعمیر موتور (کامل)", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("830d1bbc-a473-5a0b-9b3a-44bbe640540c"), "موتور", true, "part", 7, "شمع", new[] { "motorcycle" } },
                    { new Guid("8484ae8c-0881-50a6-8a84-4d95835cbd9c"), "ترمز", true, "part", 63, "لنت ترمز عقب", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("874f2c37-d2f9-5abe-8d48-8d7bcdc54462"), "اجرت و خدمات", true, "labor", 34, "تعویض روغن", new[] { "motorcycle" } },
                    { new Guid("8808f633-e978-52c7-96c1-dfe8119db58d"), "کلاچ", true, "part", 72, "دیسک و صفحه کلاچ", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("888ce6c6-6500-5f70-a7e6-3530c7f8268c"), "اجرت و خدمات", true, "labor", 44, "تعمیر استارت", new[] { "motorcycle" } },
                    { new Guid("897d4fb2-1bc6-5b6c-a1e3-2bc52dfb0c95"), "ترمز", true, "part", 15, "روغن ترمز", new[] { "motorcycle" } },
                    { new Guid("8cde9363-442d-5b8c-83c1-baf785425ebd"), "جلوبندی و تعلیق", true, "part", 68, "سیبک فرمان", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("94245bcd-15ac-5a36-adf7-47127127971d"), "روغن و فیلتر", true, "part", 49, "فیلتر کابین", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("949c69a5-b511-557c-b476-e967f53eee8b"), "روغن و فیلتر", true, "part", 53, "روغن هیدرولیک فرمان", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("999d4c93-ca26-5782-90e6-032dd4fb9f4b"), "موتور", true, "part", 10, "کاسه‌نمد", new[] { "motorcycle" } },
                    { new Guid("9eb496de-1da3-5d95-befd-cc8a9d987156"), "اجرت و خدمات", true, "labor", 41, "تعمیر کلاچ", new[] { "motorcycle" } },
                    { new Guid("a02ac3b2-3e5f-530c-a8ab-05f404c7b77b"), "ترمز", true, "part", 64, "دیسک ترمز", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("a3cde2e7-fae1-5445-a322-2e0041f00d38"), "چرخ", true, "part", 33, "بلبرینگ چرخ", new[] { "motorcycle" } },
                    { new Guid("a42df1c0-fe81-5e66-8828-47c02fc0e120"), "موتور", true, "part", 61, "واشر سرسیلندر", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("a7370e68-c0a9-54a6-94ee-6e347a01b0eb"), "اجرت و خدمات", true, "labor", 84, "تعویض کمک‌فنر", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("a9f55f0c-fdd5-53af-881f-d931f6032ed2"), "روغن و فیلتر", true, "part", 52, "ضدیخ (لیتر)", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("abb8b60c-a302-5270-a2e3-452f99d40ffc"), "برق", true, "part", 29, "استارت", new[] { "motorcycle" } },
                    { new Guid("af346c48-bdfc-51ba-a4bb-43bfcf89969d"), "موتور", true, "part", 54, "شمع", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("bac5cf5e-1a7b-5d23-a693-6e59f982e6aa"), "موتور", true, "part", 8, "واشر سرسیلندر", new[] { "motorcycle" } },
                    { new Guid("bf85bc49-75a7-5d24-9a76-f3455e70c3a2"), "انتقال قدرت", true, "part", 23, "سیم کلاچ", new[] { "motorcycle" } },
                    { new Guid("c344dab1-4933-5575-9c21-0cd5db1f8693"), "اجرت و خدمات", true, "labor", 86, "شستشوی انژکتور", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("c53992ca-d33d-5246-b11a-6e0975292c82"), "انتقال قدرت", true, "part", 18, "دنده جلو (گیربکس)", new[] { "motorcycle" } },
                    { new Guid("c8fe8879-172e-53c1-bee2-b8efd2141559"), "روغن و فیلتر", true, "part", 48, "فیلتر هوا", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("c9886a5c-df72-5dee-93dd-33115472c61f"), "اجرت و خدمات", true, "labor", 45, "تعویض یا پنچرگیری لاستیک", new[] { "motorcycle" } },
                    { new Guid("cd832d63-aa55-5f79-a024-2ce665eaa103"), "انتقال قدرت", true, "part", 19, "طبق‌دنده عقب", new[] { "motorcycle" } },
                    { new Guid("d0bc434e-b503-58ed-85da-e50b7f27f24b"), "برق", true, "part", 27, "رله راهنما", new[] { "motorcycle" } },
                    { new Guid("d3cab0ff-dc2d-581c-9f79-1b63cdb7b7ba"), "اجرت و خدمات", true, "labor", 89, "تعمیر جلوبندی", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("d3e6f513-d21a-593a-9c42-53d9127550a8"), "کلاچ", true, "part", 73, "بلبرینگ کلاچ", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("d4022fa2-d94e-5a91-ade4-d6d91fc398b5"), "اجرت و خدمات", true, "labor", 39, "تعویض کیت زنجیر", new[] { "motorcycle" } },
                    { new Guid("d537d6d5-78e0-5f81-ab39-8fb09a999975"), "اجرت و خدمات", true, "labor", 87, "شارژ گاز کولر", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("d86a16cf-a3b0-53c8-a7d5-cfee2ff42ac6"), "موتور", true, "part", 60, "واتر پمپ", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("d92e8f53-a517-5b22-8655-655ddbc6fa05"), "اجرت و خدمات", true, "labor", 93, "پنچرگیری و بالانس", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("df2fc105-4331-5bd4-833a-c731939b2e01"), "انتقال قدرت", true, "part", 22, "صفحه کلاچ", new[] { "motorcycle" } },
                    { new Guid("e35c7693-9846-5838-bc7f-c4a81fc43611"), "ترمز", true, "part", 14, "کفشک ترمز", new[] { "motorcycle" } },
                    { new Guid("e45a36e0-a5fb-5237-8cf4-3fb88af49544"), "جلوبندی و تعلیق", true, "part", 69, "طبق", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("e66d0040-984f-585d-9167-d1e54dae326f"), "موتور", true, "part", 55, "کویل", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("ef15b85a-4d88-5caa-8649-11fc46c330ea"), "برق و کولر", true, "part", 75, "لامپ چراغ جلو", new[] { "car", "suv", "van", "pickup" } },
                    { new Guid("f1e2f1ec-8485-57e4-bd20-762a91d44c0e"), "اجرت و خدمات", true, "labor", 38, "تعویض لنت", new[] { "motorcycle" } },
                    { new Guid("f45caf56-fde0-5413-bf52-bf22d743ab00"), "ترمز", true, "part", 16, "سیم ترمز", new[] { "motorcycle" } },
                    { new Guid("fcedaaf1-2fc7-5604-8f86-1d60c825cb61"), "روغن و فیلتر", true, "part", 1, "روغن موتور", new[] { "motorcycle" } }
                });

            migrationBuilder.InsertData(
                table: "ServicePackageLines",
                columns: new[] { "Id", "Optional", "PackageId", "Quantity", "SortOrder", "StandardItemId" },
                values: new object[,]
                {
                    { new Guid("01af93a0-bcd5-5cd2-85d3-2e3c0a40f3a7"), false, new Guid("48204068-ffca-5cc3-8147-cf572e00b6de"), 1m, 4, new Guid("830d1bbc-a473-5a0b-9b3a-44bbe640540c") },
                    { new Guid("04f8ef38-9572-5bcd-86cc-ae01172216b6"), false, new Guid("ef8f6291-db2a-5b00-b372-e168f23b323a"), 1m, 1, new Guid("38de0d47-a736-5ac6-ae5b-72a9e73a754c") },
                    { new Guid("07f12b50-c310-589e-9753-d2ca3f365c8a"), false, new Guid("2ce1b3fe-fd59-569c-8662-5b866d3f8299"), 1m, 3, new Guid("c8fe8879-172e-53c1-bee2-b8efd2141559") },
                    { new Guid("0bead920-b11b-52bb-9ebb-b89dcde3bc56"), false, new Guid("41fe2c73-22b5-5355-aab1-c161a08ddec8"), 1m, 1, new Guid("763db814-1148-5fb2-b116-6f02fb4ca0aa") },
                    { new Guid("0f207472-c3db-5bd3-958a-eb6877ee29ce"), false, new Guid("2ce1b3fe-fd59-569c-8662-5b866d3f8299"), 1m, 6, new Guid("4d8533eb-a1e1-5de1-98c1-ba82f575f1fb") },
                    { new Guid("1909a7dd-d459-58df-a1d8-a0f99eacbbb9"), false, new Guid("d5d9888f-fdac-5261-b8c8-d9153d15915c"), 1m, 4, new Guid("10564b98-6bcd-55e3-90e0-68d81f7f8ff4") },
                    { new Guid("1da396a0-78de-56cb-8186-451cbcb4ad79"), true, new Guid("2ce1b3fe-fd59-569c-8662-5b866d3f8299"), 4m, 5, new Guid("af346c48-bdfc-51ba-a4bb-43bfcf89969d") },
                    { new Guid("310e9ea2-6387-51fb-aedb-a1a41bb853bb"), false, new Guid("48204068-ffca-5cc3-8147-cf572e00b6de"), 1m, 5, new Guid("55b7ea30-15cf-536e-ba93-e4fb0e566ea0") },
                    { new Guid("38300452-1a53-5031-9fa4-3be1ddc8f715"), false, new Guid("48204068-ffca-5cc3-8147-cf572e00b6de"), 1m, 2, new Guid("4cd01be1-34ac-5dbc-ade3-5b4f6d3f81a2") },
                    { new Guid("3cd7c865-17c5-5360-bb73-eff078291016"), false, new Guid("2ce1b3fe-fd59-569c-8662-5b866d3f8299"), 1m, 2, new Guid("38ce647d-75d4-5fa8-ab8d-d523971935a1") },
                    { new Guid("3ce6f297-d32c-5149-b647-d14f31aa61bf"), false, new Guid("ef8f6291-db2a-5b00-b372-e168f23b323a"), 1m, 4, new Guid("f1e2f1ec-8485-57e4-bd20-762a91d44c0e") },
                    { new Guid("49f7d4e2-4fe2-5f04-bced-5338cb3a35d3"), false, new Guid("2ce1b3fe-fd59-569c-8662-5b866d3f8299"), 1m, 4, new Guid("94245bcd-15ac-5a36-adf7-47127127971d") },
                    { new Guid("4b6a9b95-c579-5fbc-80d3-73d0357c7e18"), false, new Guid("929594f9-d855-5b9c-8de9-8e359a91053d"), 1m, 2, new Guid("016c200e-08f2-5f0c-bf67-3038cd6e5f4c") },
                    { new Guid("4e2a7b9a-aa56-5376-8c6e-bc7b1d840100"), true, new Guid("ef8f6291-db2a-5b00-b372-e168f23b323a"), 1m, 3, new Guid("897d4fb2-1bc6-5b6c-a1e3-2bc52dfb0c95") },
                    { new Guid("4e33ef31-a82b-5aa5-8566-e44ccf79da6d"), true, new Guid("033f95cc-c94f-5c3a-967c-a588a6535a56"), 1m, 2, new Guid("4cd01be1-34ac-5dbc-ade3-5b4f6d3f81a2") },
                    { new Guid("53cbd58e-fac3-5d08-9453-fa829db8c576"), false, new Guid("929594f9-d855-5b9c-8de9-8e359a91053d"), 1m, 1, new Guid("230c04b8-8369-5b8f-ac71-9bf0a1b80f7a") },
                    { new Guid("76bee6a6-c864-5285-9a78-a670c83ddd27"), false, new Guid("a2c0b851-b614-50e4-bd27-0010ad9afdf4"), 1m, 3, new Guid("cd832d63-aa55-5f79-a024-2ce665eaa103") },
                    { new Guid("868a3f6b-051b-5fe6-acc1-5b9a4b4ac8fa"), false, new Guid("033f95cc-c94f-5c3a-967c-a588a6535a56"), 1m, 3, new Guid("874f2c37-d2f9-5abe-8d48-8d7bcdc54462") },
                    { new Guid("86ddac1d-2015-5b09-b919-ada88b39fc95"), true, new Guid("ef8f6291-db2a-5b00-b372-e168f23b323a"), 1m, 2, new Guid("1583fe4c-28e7-5bbd-b407-2c7452b8e489") },
                    { new Guid("89b428de-59f2-5efa-83a1-bb95a6d42f28"), false, new Guid("2ce1b3fe-fd59-569c-8662-5b866d3f8299"), 4m, 1, new Guid("5444af96-0ffc-5a95-92e3-e0f312af708b") },
                    { new Guid("8da273bd-4ae6-5929-9060-9fd0ad101fb0"), false, new Guid("65e96321-1f3b-5e8c-b019-7771bc44d927"), 4m, 1, new Guid("5444af96-0ffc-5a95-92e3-e0f312af708b") },
                    { new Guid("96549daf-5db7-5fc2-a2a9-b25f330a8b39"), false, new Guid("48204068-ffca-5cc3-8147-cf572e00b6de"), 1m, 3, new Guid("085128d6-c25b-5885-822b-0e8b9f03d341") },
                    { new Guid("9de01799-b603-5752-b004-52c56c471404"), false, new Guid("65e96321-1f3b-5e8c-b019-7771bc44d927"), 1m, 2, new Guid("38ce647d-75d4-5fa8-ab8d-d523971935a1") },
                    { new Guid("a7bc52e1-1eb7-5641-a001-973d9306aea5"), false, new Guid("a2c0b851-b614-50e4-bd27-0010ad9afdf4"), 1m, 2, new Guid("c53992ca-d33d-5246-b11a-6e0975292c82") },
                    { new Guid("a95df5d4-c9f0-5f74-a271-39ce1c6c75f9"), true, new Guid("41fe2c73-22b5-5355-aab1-c161a08ddec8"), 1m, 2, new Guid("5e28aba4-dadd-5994-a843-97c280563b8f") },
                    { new Guid("aafd6d13-31c4-5321-af00-ed8f743e44df"), false, new Guid("e61998ad-e4e0-5c1c-a041-37f2f31daff1"), 1m, 2, new Guid("d537d6d5-78e0-5f81-ab39-8fb09a999975") },
                    { new Guid("abde802a-5f4a-5dbe-83e3-fe6f6f3c2a98"), false, new Guid("e61998ad-e4e0-5c1c-a041-37f2f31daff1"), 1m, 1, new Guid("7eb65ddc-e5c9-5b2d-baeb-18a72a609282") },
                    { new Guid("ac29bdef-aa12-58e7-a765-e553e850002f"), false, new Guid("48204068-ffca-5cc3-8147-cf572e00b6de"), 1m, 1, new Guid("fcedaaf1-2fc7-5604-8f86-1d60c825cb61") },
                    { new Guid("ada5d327-a40d-574a-a18f-fc8a4e658a61"), false, new Guid("d5d9888f-fdac-5261-b8c8-d9153d15915c"), 1m, 1, new Guid("3f131158-7423-51a4-8c90-7962fd46881f") },
                    { new Guid("b1979f91-5f30-5576-a1c2-5dfe37b86acc"), false, new Guid("65e96321-1f3b-5e8c-b019-7771bc44d927"), 1m, 3, new Guid("60617ee8-587a-5c58-b75d-d2ccf0bf5dcb") },
                    { new Guid("be2a86b4-17e5-5f8d-971f-5b66a8db1a2c"), false, new Guid("929594f9-d855-5b9c-8de9-8e359a91053d"), 1m, 5, new Guid("2cc83a9f-07f8-5875-ab03-e4ec1ecd929c") },
                    { new Guid("c32b42b2-3c19-51b8-876e-5a4b3f0df20f"), false, new Guid("033f95cc-c94f-5c3a-967c-a588a6535a56"), 1m, 1, new Guid("fcedaaf1-2fc7-5604-8f86-1d60c825cb61") },
                    { new Guid("c4622481-77c3-557b-93e2-fcf5792cd1eb"), true, new Guid("929594f9-d855-5b9c-8de9-8e359a91053d"), 1m, 4, new Guid("25759973-2e38-5e05-ac65-b93ccebaf014") },
                    { new Guid("cb57d78e-f9d1-5661-bdf7-4ea53886676f"), true, new Guid("d5d9888f-fdac-5261-b8c8-d9153d15915c"), 1m, 3, new Guid("39942727-73cb-5b81-8983-f5f6832a0a9e") },
                    { new Guid("d60c7fba-25c2-56a6-a80a-2430cd055ca1"), false, new Guid("a2c0b851-b614-50e4-bd27-0010ad9afdf4"), 1m, 1, new Guid("605fdba7-132d-5183-858e-a113efd2d63b") },
                    { new Guid("eb003261-4f18-57c1-840c-7d169dca4978"), true, new Guid("d5d9888f-fdac-5261-b8c8-d9153d15915c"), 2m, 2, new Guid("a02ac3b2-3e5f-530c-a8ab-05f404c7b77b") },
                    { new Guid("f7d9b841-e970-5337-8b2f-cf169e0dd18d"), false, new Guid("a2c0b851-b614-50e4-bd27-0010ad9afdf4"), 1m, 4, new Guid("d4022fa2-d94e-5a91-ade4-d6d91fc398b5") },
                    { new Guid("f8c66122-a1f4-52c3-80fe-5756f986f4ae"), false, new Guid("41fe2c73-22b5-5355-aab1-c161a08ddec8"), 1m, 3, new Guid("517a77af-6ea9-55cb-919d-191e9631ba77") },
                    { new Guid("fc7dd368-764e-5dec-a07c-eb2847e5345b"), true, new Guid("929594f9-d855-5b9c-8de9-8e359a91053d"), 1m, 3, new Guid("d86a16cf-a3b0-53c8-a7d5-cfee2ff42ac6") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_StandardItemId",
                table: "CatalogItems",
                column: "StandardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_TenantId_StandardItemId",
                table: "CatalogItems",
                columns: new[] { "TenantId", "StandardItemId" },
                unique: true,
                filter: "\"StandardItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePackageLines_PackageId_SortOrder",
                table: "ServicePackageLines",
                columns: new[] { "PackageId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePackageLines_StandardItemId",
                table: "ServicePackageLines",
                column: "StandardItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StandardItems_SortOrder",
                table: "StandardItems",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogItems_StandardItems_StandardItemId",
                table: "CatalogItems",
                column: "StandardItemId",
                principalTable: "StandardItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatalogItems_StandardItems_StandardItemId",
                table: "CatalogItems");

            migrationBuilder.DropTable(
                name: "ServicePackageLines");

            migrationBuilder.DropTable(
                name: "ServicePackages");

            migrationBuilder.DropTable(
                name: "StandardItems");

            migrationBuilder.DropIndex(
                name: "IX_CatalogItems_StandardItemId",
                table: "CatalogItems");

            migrationBuilder.DropIndex(
                name: "IX_CatalogItems_TenantId_StandardItemId",
                table: "CatalogItems");

            migrationBuilder.DropColumn(
                name: "StandardItemId",
                table: "CatalogItems");
        }
    }
}
