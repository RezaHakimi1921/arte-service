using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Core.Common;
using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class StaffPayReportTests(ArteApiFactory api)
{
    private static readonly PersianCalendar Calendar = new();

    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static DateOnly JalaliDay(int year, int month, int day) =>
        DateOnly.FromDateTime(Calendar.ToDateTime(year, month, day, 0, 0, 0, 0));

    private static DateTimeOffset Tehran(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), Jalali.TehranOffset);

    private static string PayReport(DateTimeOffset from, DateTimeOffset to) =>
        $"/api/v1/reports/staff-pay?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

    private static JsonElement StaffRow(JsonElement report, Guid membershipId) =>
        report.GetProperty("staff").EnumerateArray().Single(s => s.GetProperty("membershipId").GetGuid() == membershipId);

    private async Task<Guid> AddTech(HttpClient owner, string role = "technician")
    {
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = ArteApiFactory.NewMobile(), role }));
        return created.GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Owner, Guid TenantId, Guid TechId, Guid CaseId, JsonElement Detail)> DeliveredCase(object pay)
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", pay)).StatusCode);

        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), reportedProblems = new[] { "روشن نمی‌شود" }, assigneeId = techId,
            newAsset = new { title = "پژو ۲۰۶", kind = "car" },
        }));
        var id = created.GetProperty("id").GetGuid();
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "قطعات", unitCostRials = 25_000_000, unitPriceRials = 30_000_000 }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "labor", title = "اجرت", unitPriceRials = 10_000_000 }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 40_000_000, method = "cash" }));
        var detail = await WalkToDelivered(owner, id);
        return (owner, tenantId, techId, id, detail);
    }

    private static async Task<JsonElement> WalkToDelivered(HttpClient owner, Guid id)
    {
        var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        while (c.GetProperty("stage").GetProperty("key").GetString() != "delivered")
        {
            var primary = c.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
            c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{primary}", new { }));
        }
        return c;
    }

    [Fact]
    public async Task Fixed_salary_is_prorated_by_real_jalali_month_length_and_inclusive_employment()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        const long monthly = 3_100_000;
        var dim = Calendar.GetDaysInMonth(1404, 1);
        Assert.Equal(31, dim);
        var monthStart = JalaliDay(1404, 1, 1);
        var day11 = JalaliDay(1404, 1, 11);
        var day15 = JalaliDay(1404, 1, 15);
        var nextMonth = JalaliDay(1404, 2, 1);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            fixedMonthlyRials = monthly, employmentStartedOn = monthStart, displayName = "رضا",
        })).StatusCode);

        var tenDays = StaffRow(await Json(await owner.GetAsync(PayReport(Tehran(monthStart), Tehran(day11)))), techId);
        var tenDayPay = (long)Math.Round(monthly * 10m / dim, MidpointRounding.AwayFromZero);
        var asIfThirtyDayMonth = (long)Math.Round(monthly * 10m / 30, MidpointRounding.AwayFromZero);
        Assert.NotEqual(tenDayPay, asIfThirtyDayMonth);
        Assert.Equal(1_000_000, tenDayPay);
        Assert.Equal(tenDayPay, tenDays.GetProperty("fixedSalaryRials").GetInt64());
        Assert.Equal("رضا", tenDays.GetProperty("name").GetString());
        Assert.Equal(monthStart.ToString("yyyy-MM-dd"), tenDays.GetProperty("employmentStartedOn").GetString());
        Assert.Equal(JsonValueKind.Null, tenDays.GetProperty("employmentEndedOn").ValueKind);

        var listed = (await Json(await owner.GetAsync("/api/v1/staff"))).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == techId);
        Assert.Equal(monthStart.ToString("yyyy-MM-dd"), listed.GetProperty("employmentStartedOn").GetString());

        // End day is inclusive: day 1 through day 10 is ten days, not nine.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            employmentEndedOn = JalaliDay(1404, 1, 10),
        })).StatusCode);
        var ended = StaffRow(await Json(await owner.GetAsync(PayReport(Tehran(monthStart), Tehran(nextMonth)))), techId);
        Assert.Equal(1_000_000, ended.GetProperty("fixedSalaryRials").GetInt64());
        Assert.Equal(JalaliDay(1404, 1, 10).ToString("yyyy-MM-dd"), ended.GetProperty("employmentEndedOn").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            employmentEndedOn = monthStart.AddDays(-1),
        })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            employmentStartedOn = day15, employmentEndedOn = day15,
        })).StatusCode);
        var oneDay = StaffRow(await Json(await owner.GetAsync(PayReport(Tehran(monthStart), Tehran(nextMonth)))), techId);
        Assert.Equal(100_000, oneDay.GetProperty("fixedSalaryRials").GetInt64());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            employmentStartedOn = monthStart, clearEmploymentEndedOn = true,
        })).StatusCode);
        var full = StaffRow(await Json(await owner.GetAsync(PayReport(Tehran(monthStart), Tehran(nextMonth)))), techId);
        Assert.Equal(monthly, full.GetProperty("fixedSalaryRials").GetInt64());
        Assert.Equal(JsonValueKind.Null, full.GetProperty("employmentEndedOn").ValueKind);
    }

    [Fact]
    public async Task Commission_is_recorded_once_on_the_first_delivery()
    {
        var (owner, tenantId, techId, caseId, delivered) = await DeliveredCase(new
        {
            commissionType = "fixed_per_case", commissionFixedRials = 1_500_000, fixedMonthlyRials = 150_000_000, displayName = "حسین",
        });
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            commissionFixedRials = 9_000_000,
        })).StatusCode);

        var reopen = delivered.GetProperty("transitions").EnumerateArray().Single(t => t.GetProperty("requiresReason").GetBoolean());
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{caseId}/transitions/{reopen.GetProperty("id").GetGuid()}", new { reason = "بازگشایی برای آزمون" }));
        await WalkToDelivered(owner, caseId);

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);
        var row = StaffRow(await Json(await owner.GetAsync(PayReport(from, to))), techId);
        Assert.Equal(1, row.GetProperty("cases").GetInt32());
        Assert.Equal(1_500_000, row.GetProperty("commissionRials").GetInt64());
        Assert.Equal(1_500_000, row.GetProperty("commissionCases").EnumerateArray().Single().GetProperty("commissionRials").GetInt64());
        Assert.Equal(caseId, row.GetProperty("commissionCases")[0].GetProperty("caseId").GetGuid());
        var income = row.GetProperty("fixedSalaryRials").GetInt64() + row.GetProperty("commissionRials").GetInt64() + row.GetProperty("bonusRials").GetInt64();
        Assert.Equal(income, row.GetProperty("incomeRials").GetInt64());

        var summary = await Json(await owner.GetAsync($"/api/v1/reports/summary?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}"));
        var summaryRow = summary.GetProperty("staff").EnumerateArray().Single(s => s.GetProperty("membershipId").GetGuid() == techId);
        Assert.Equal(1, summaryRow.GetProperty("cases").GetInt32());
        Assert.Equal(1_500_000, summaryRow.GetProperty("commissionRials").GetInt64());
        Assert.Equal(150_000_000, summaryRow.GetProperty("fixedMonthlyRials").GetInt64());
        Assert.InRange(summaryRow.GetProperty("fixedSalaryRials").GetInt64(), 1, 149_999_999);

        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Arte.Core.Tenancy.TenantContext>().Set(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        Assert.Equal(1, await db.CaseCommissionGrants.CountAsync(g => g.CaseId == caseId));
    }

    [Fact]
    public async Task Bonus_overlapping_one_day_counts_the_whole_jalali_month()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        var start = JalaliDay(1404, 10, 1);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new { employmentStartedOn = start })).StatusCode);
        await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = techId, amountRials = 2_000_000, title = "عیدی", jalaliYear = 1404, jalaliMonth = 10,
        }));
        await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = techId, amountRials = 500_000, title = "پاداش کیفیت", jalaliYear = 1404, jalaliMonth = 10,
        }));

        var oneDay = Tehran(JalaliDay(1404, 10, 15));
        var row = StaffRow(await Json(await owner.GetAsync(PayReport(oneDay, oneDay.AddDays(1)))), techId);
        Assert.Equal(2_500_000, row.GetProperty("bonusRials").GetInt64());
        Assert.Equal(2, row.GetProperty("bonuses").GetArrayLength());
        Assert.Equal(2_500_000, row.GetProperty("incomeRials").GetInt64());

        var nextMonth = Tehran(JalaliDay(1404, 11, 1));
        var outside = StaffRow(await Json(await owner.GetAsync(PayReport(nextMonth, nextMonth.AddDays(1)))), techId);
        Assert.Equal(0, outside.GetProperty("bonusRials").GetInt64());
        Assert.Empty(outside.GetProperty("bonuses").EnumerateArray());
    }

    [Fact]
    public async Task Shop_loss_uses_case_open_date_and_is_not_deducted_from_pay()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        var opened = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), request = "زیان", assigneeId = techId,
        }));
        var caseId = opened.GetProperty("id").GetGuid();
        var openedAt = Tehran(JalaliDay(1404, 10, 5)).AddHours(4).ToUniversalTime();
        await using (var scope = api.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<Arte.Core.Tenancy.TenantContext>().Set(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
            Assert.Equal(1, await db.Cases.Where(c => c.Id == caseId).ExecuteUpdateAsync(s => s.SetProperty(c => c.OpenedAt, openedAt)));
        }

        await Json(await owner.PostAsJsonAsync("/api/v1/shop-losses", new { amountRials = 700_000, responsibleUserId = techId, caseId }));
        await Json(await owner.PostAsJsonAsync("/api/v1/shop-losses", new { amountRials = 900_000, responsibleUserId = techId }));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            employmentStartedOn = JalaliDay(1404, 10, 1),
        })).StatusCode);

        var monthStart = Tehran(JalaliDay(1404, 10, 1));
        var historical = StaffRow(await Json(await owner.GetAsync(PayReport(monthStart, Tehran(JalaliDay(1404, 11, 1))))), techId);
        Assert.Equal(1, historical.GetProperty("shopLossCount").GetInt32());
        Assert.Equal(700_000, historical.GetProperty("shopLossRials").GetInt64());
        Assert.Equal(historical.GetProperty("incomeRials").GetInt64(), historical.GetProperty("afterDeductionsRials").GetInt64());
        Assert.NotEqual(historical.GetProperty("incomeRials").GetInt64() - 700_000, historical.GetProperty("afterDeductionsRials").GetInt64());

        var nowFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var nowTo = DateTimeOffset.UtcNow.AddDays(1);
        var current = StaffRow(await Json(await owner.GetAsync(PayReport(nowFrom, nowTo))), techId);
        Assert.Equal(1, current.GetProperty("shopLossCount").GetInt32());
        Assert.Equal(900_000, current.GetProperty("shopLossRials").GetInt64());
    }

    [Fact]
    public async Task Deduction_cannot_pass_that_months_income_and_does_not_carry_over()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = techId, amountRials = 1_000_000, title = "پاداش ماه", jalaliYear = 1404, jalaliMonth = 8,
        }));

        var emptyMonth = await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 1, category = "advance", jalaliYear = 1404, jalaliMonth = 7,
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyMonth.StatusCode);
        Assert.Contains("amountRials", await emptyMonth.Content.ReadAsStringAsync());

        await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 600_000, category = "advance", jalaliYear = 1404, jalaliMonth = 8,
        }));
        var over = await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 500_000, category = "financial_adjustment", jalaliYear = 1404, jalaliMonth = 8,
        });
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);

        var second = await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 400_000, category = "financial_adjustment", jalaliYear = 1404, jalaliMonth = 8,
        }));
        var raised = await owner.PatchAsJsonAsync($"/api/v1/reports/staff-pay/deductions/{second.GetProperty("id").GetGuid()}", new { amountRials = 500_000 });
        Assert.Equal(HttpStatusCode.BadRequest, raised.StatusCode);

        var ownerId = (await Json(await owner.GetAsync("/api/v1/staff"))).EnumerateArray()
            .Single(s => s.GetProperty("role").GetString() == "owner").GetProperty("id").GetGuid();
        var foreignLoss = await Json(await owner.PostAsJsonAsync("/api/v1/shop-losses", new { amountRials = 50_000, responsibleUserId = ownerId }));
        var wrongPerson = await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 1, category = "loss_linked", jalaliYear = 1404, jalaliMonth = 8,
            shopLossId = foreignLoss.GetProperty("id").GetGuid(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPerson.StatusCode);
        Assert.Contains("shopLossId", await wrongPerson.Content.ReadAsStringAsync());

        var from = Tehran(JalaliDay(1404, 8, 1));
        var row = StaffRow(await Json(await owner.GetAsync(PayReport(from, Tehran(JalaliDay(1404, 9, 1))))), techId);
        Assert.Equal(1_000_000, row.GetProperty("deductionRials").GetInt64());
        Assert.Equal(1_000_000, row.GetProperty("incomeRials").GetInt64());
        Assert.Equal(0, row.GetProperty("afterDeductionsRials").GetInt64());
    }

    [Fact]
    public async Task Void_drops_bonus_and_deduction_from_totals_and_keeps_an_audit_row()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var (other, otherTenant) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        var bonus = await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = techId, amountRials = 2_000_000, title = "پاداش", jalaliYear = 1404, jalaliMonth = 6,
        }));
        var bonusId = bonus.GetProperty("id").GetGuid();
        var deduction = await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 500_000, category = "advance", jalaliYear = 1404, jalaliMonth = 6,
        }));
        var deductionId = deduction.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/reports/staff-pay/bonuses/{bonusId}/void", new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/v1/reports/staff-pay/bonuses/{bonusId}/void", new { reason = "اشتباه ثبت شد" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/reports/staff-pay/bonuses/{bonusId}/void", new { reason = "دوباره" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/reports/staff-pay/bonuses/{bonusId}/void", new { reason = "غریبه" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/v1/reports/staff-pay/deductions/{deductionId}/void", new { reason = "بخشیده شد" })).StatusCode);

        var from = Tehran(JalaliDay(1404, 6, 1));
        var row = StaffRow(await Json(await owner.GetAsync(PayReport(from, Tehran(JalaliDay(1404, 7, 1))))), techId);
        Assert.Equal(0, row.GetProperty("bonusRials").GetInt64());
        Assert.Equal(0, row.GetProperty("deductionRials").GetInt64());
        Assert.Equal(2_000_000, row.GetProperty("bonuses").EnumerateArray().Single().GetProperty("amountRials").GetInt64());
        Assert.Equal(JsonValueKind.String, row.GetProperty("bonuses")[0].GetProperty("voidedAt").ValueKind);
        Assert.Equal(0, row.GetProperty("incomeRials").GetInt64());

        // The voided bonus no longer funds a deduction.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 1, category = "advance", jalaliYear = 1404, jalaliMonth = 6,
        })).StatusCode);

        await using var scope = api.Services.CreateAsyncScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<Arte.Core.Tenancy.TenantContext>();
        tenantContext.Set(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var logs = await db.StaffPayAuditLogs.OrderBy(a => a.Id).ToListAsync();
        Assert.Contains(logs, a => a.Action == "create" && a.EntityType == "bonus");
        Assert.Contains(logs, a => a.Action == "void" && a.EntityType == "bonus" && a.Reason == "اشتباه ثبت شد");
        Assert.Contains(logs, a => a.Action == "void" && a.EntityType == "deduction");
        Assert.Equal(2, logs.Count(a => a.Action == "void"));
        Assert.All(logs, a => Assert.False(string.IsNullOrEmpty(a.AfterJson)));

        await using var otherScope = api.Services.CreateAsyncScope();
        otherScope.ServiceProvider.GetRequiredService<Arte.Core.Tenancy.TenantContext>().Set(otherTenant);
        var otherDb = otherScope.ServiceProvider.GetRequiredService<ArteDbContext>();
        Assert.Empty(await otherDb.StaffPayAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Settlement_snapshots_the_month_once_and_does_not_subtract_shop_loss()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        var today = Jalali.ToTehranDate(DateTimeOffset.UtcNow);
        var (year, month, _) = Jalali.Parts(today);
        var monthStart = JalaliDay(year, month, 1);
        var next = month == 12 ? JalaliDay(year + 1, 1, 1) : JalaliDay(year, month + 1, 1);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new
        {
            fixedMonthlyRials = 3_000_000, employmentStartedOn = monthStart,
        })).StatusCode);
        await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = techId, amountRials = 2_000_000, title = "پاداش ماه", jalaliYear = year, jalaliMonth = month,
        }));
        await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = techId, amountRials = 500_000, category = "advance", jalaliYear = year, jalaliMonth = month,
        }));
        await Json(await owner.PostAsJsonAsync("/api/v1/shop-losses", new { amountRials = 700_000, responsibleUserId = techId }));

        var confirmed = await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/settlements", new
        {
            membershipId = techId, jalaliYear = year, jalaliMonth = month,
        }));
        Assert.Equal(3_000_000, confirmed.GetProperty("fixedSalaryRials").GetInt64());
        Assert.Equal(2_000_000, confirmed.GetProperty("bonusRials").GetInt64());
        Assert.Equal(500_000, confirmed.GetProperty("deductionRials").GetInt64());
        Assert.Equal(700_000, confirmed.GetProperty("shopLossRials").GetInt64());
        Assert.Equal(5_000_000, confirmed.GetProperty("incomeRials").GetInt64());
        Assert.Equal(4_500_000, confirmed.GetProperty("afterDeductionsRials").GetInt64());

        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/settlements", new
        {
            membershipId = techId, jalaliYear = year, jalaliMonth = month,
        })).StatusCode);

        var row = StaffRow(await Json(await owner.GetAsync(PayReport(Tehran(monthStart), Tehran(next)))), techId);
        Assert.Equal(4_500_000, row.GetProperty("settlementsInRange").EnumerateArray().Single().GetProperty("afterDeductionsRials").GetInt64());

        var previous = month == 1 ? (year - 1, 12) : (year, month - 1);
        var previousStart = JalaliDay(previous.Item1, previous.Item2, 1);
        var earlier = await Json(await owner.GetAsync(PayReport(Tehran(previousStart), Tehran(monthStart))));
        Assert.DoesNotContain(earlier.GetProperty("staff").EnumerateArray(), s =>
            s.GetProperty("membershipId").GetGuid() == techId && s.GetProperty("settlementsInRange").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Staff_pay_reads_need_reports_view_and_writes_need_staff_manage()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" });
        var supervisorMobile = ArteApiFactory.NewMobile();
        await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = supervisorMobile, role = "supervisor" });
        var (tech, _) = await api.LoginAsync(techMobile);
        var (supervisor, _) = await api.LoginAsync(supervisorMobile);
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var url = $"/api/v1/reports/staff-pay?from={from}&to={to}";

        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = Guid.NewGuid(), amountRials = 1, title = "x", jalaliYear = 1404, jalaliMonth = 1,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await supervisor.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PostAsJsonAsync("/api/v1/reports/staff-pay/deductions", new
        {
            membershipId = Guid.NewGuid(), amountRials = 1, category = "advance", jalaliYear = 1404, jalaliMonth = 1,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PostAsJsonAsync("/api/v1/reports/staff-pay/settlements", new
        {
            membershipId = Guid.NewGuid(), jalaliYear = 1404, jalaliMonth = 1,
        })).StatusCode);
    }

    [Fact]
    public async Task Another_tenant_cannot_read_or_change_staff_pay()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var techId = await AddTech(owner);
        var bonus = await Json(await owner.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = techId, amountRials = 100_000, title = "پاداش", jalaliYear = 1404, jalaliMonth = 10,
        }));
        var bonusId = bonus.GetProperty("id").GetGuid();
        var from = Tehran(JalaliDay(1404, 10, 1));
        var to = Tehran(JalaliDay(1404, 11, 1));

        var theirs = await Json(await other.GetAsync(PayReport(from, to)));
        Assert.DoesNotContain(theirs.GetProperty("staff").EnumerateArray(), s => s.GetProperty("membershipId").GetGuid() == techId);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/reports/staff-pay/bonuses/{bonusId}/void", new { reason = "نه" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsJsonAsync($"/api/v1/reports/staff-pay/bonuses/{bonusId}", new { amountRials = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/v1/reports/staff-pay/settlements", new
        {
            membershipId = techId, jalaliYear = 1404, jalaliMonth = 10,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/v1/reports/staff-pay/bonuses", new
        {
            membershipId = techId, amountRials = 1, title = "نفوذ", jalaliYear = 1404, jalaliMonth = 10,
        })).StatusCode);
    }
}
