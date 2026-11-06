using System.Security.Cryptography;
using System.Text;
using Arte.Api.Options;
using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Tenancy;
using Arte.Core.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arte.Api.Tenants;

public static class TenantEndpoints
{
    public sealed record CreateTenant(string? Name, string? Phone, string? InviteCode, string? OwnerName, string? BusinessType = null);
    public sealed record BusinessSettings(string? Name, string? Phone, string? Address, bool? RequireAssigneeOnIntake,
        bool? RequireCustomerApproval, bool? RequireFinalReview, string? BusinessType = null, string[]? VehicleKinds = null,
        bool? CustomerSmsEnabled = null, bool? SmsOnOpened = null, bool? SmsOnReady = null, bool? SmsOnDelivered = null,
        bool? TrackShowStages = null, bool? TrackShowItems = null, bool? TrackShowAmounts = null, bool? PhotosVisibleByDefault = null,
        bool? SurveySendOn = null, int? SurveyDelayMinutes = null, int? SurveyAlertBelow = null, bool? RequireTransferReceipt = null,
        bool? RequirePaymentBeforeWork = null, bool? TrackShowPhone = null, string? InstagramId = null, string? MapNeshanUrl = null,
        string? MapGoogleUrl = null, string? MapWazeUrl = null, string? MapBaladUrl = null, bool? SmsOnWaitingPart = null, bool? SmsToMasterOnFinished = null);

    /// <summary>An https link on the app's own domain (or a subdomain), up to 300 characters: it is shown to customers, so nothing else is accepted.</summary>
    internal static bool ValidMapUrl(string url, string[] hosts) =>
        url.Length <= 300 && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && hosts.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    /// <summary>The logo file for a stored relative path, or 404 when none is set or the file is missing.</summary>
    internal static IResult LogoFile(string? relative, IConfiguration config, IHostEnvironment env)
    {
        if (string.IsNullOrEmpty(relative)) return Results.NotFound();
        var root = Arte.Api.Cases.AttachmentEndpoints.Root(config, env);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(rootWithSeparator, StringComparison.Ordinal) || !File.Exists(full)) return Results.NotFound();
        var type = Path.GetExtension(full).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return Results.File(full, type);
    }

    public static void MapTenants(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", async (RequestUser me, ArteDbContext db, IConfiguration config, IClock clock, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == me.RequiredUserId, ct);
            var m = me.Membership;
            return Results.Ok(new
            {
                user.Id,
                user.Mobile,
                DisplayName = m?.DisplayName ?? user.DisplayName,
                user.IsPlatformAdmin,
                // More than one branch: the app opens on the branches dashboard.
                BranchCount = await db.Memberships.IgnoreQueryFilters([ArteDbContext.TenantFilter]).CountAsync(x => x.UserId == user.Id && x.IsActive, ct),
                OpenMode = config.GetValue("Auth:OpenMode", false),
                Business = m is null ? null : new
                {
                    m.TenantId,
                    Name = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.Name).SingleAsync(ct),
                    Brand = await (from t in db.Tenants where t.Id == m.TenantId
                                   join o in db.Organizations on t.OrganizationId equals o.Id
                                   select o.Name).FirstOrDefaultAsync(ct),
                    RequireAssigneeOnIntake = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.RequireAssigneeOnIntake).SingleAsync(ct),
                    BusinessType = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.BusinessType).SingleAsync(ct),
                    VehicleKinds = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.VehicleKinds).SingleAsync(ct),
                    m.Role,
                    Permissions = m.Role == Roles.Owner ? [.. Permissions.All] : m.Permissions,
                    IsActive = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.IsActive).SingleAsync(ct),
                    // Arte support access: the app shows a banner.
                    Support = m.IsSupport,
                    SurveyEnabled = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.SurveyEnabled).SingleAsync(ct),
                    License = await Arte.Api.Licensing.LicenseService.StatusAsync(db, m.TenantId, clock.UtcNow, ct),
                    TourDone = await db.Memberships.IgnoreQueryFilters([ArteDbContext.SupportFilter]).Where(x => x.Id == m.Id).Select(x => x.TourDoneAt != null).SingleAsync(ct),
                    SampleCaseId = await db.Cases.Where(c => c.IsSample).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct),
                },
            });
        }).RequireAuthorization();

        app.MapGet("/api/v1/settings/business", async (RequestUser me, ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Tenants.AsNoTracking().Where(t => t.Id == me.RequiredMembership.TenantId)
                .Select(t => new
                {
                    t.Name, t.Phone, t.Address, t.RequireAssigneeOnIntake, t.RequireCustomerApproval, t.RequireFinalReview,
                    t.BusinessType, t.VehicleKinds, t.CustomerSmsEnabled, t.SmsOnOpened, t.SmsOnReady, t.SmsOnDelivered,
                    t.TrackShowStages, t.TrackShowItems, t.TrackShowAmounts, t.PhotosVisibleByDefault,
                    t.SurveyEnabled, t.SurveySendOn, t.SurveyDelayMinutes, t.SurveyAlertBelow, t.RequireTransferReceipt, t.RequirePaymentBeforeWork, t.TrackShowPhone, t.InstagramId, t.MapNeshanUrl, t.MapGoogleUrl, t.MapWazeUrl, t.MapBaladUrl, t.SmsOnWaitingPart, t.SmsToMasterOnFinished, HasLogo = t.LogoPath != null,
                }).SingleAsync(ct)))
            .RequirePermission(Permissions.SettingsManage);

        // The shop's logo: one small image, replaced or removed (the old file stays on disk, only the link is cleared).
        app.MapPost("/api/v1/settings/business/logo", async (HttpRequest request, RequestUser me, ArteDbContext db, Audit audit,
            IConfiguration config, IHostEnvironment env, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.Problem(statusCode: 400, title: "فایلی ارسال نشده است.");
            var file = (await request.ReadFormAsync(ct)).Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.Problem(statusCode: 400, title: "فایلی ارسال نشده است.");
            if (file.Length > 1_000_000) return Results.Problem(statusCode: 400, title: "حجم لوگو حداکثر ۱ مگابایت است.");
            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (Arte.Api.Cases.AttachmentEndpoints.Sniff(bytes) is not { } kind)
                return Results.Problem(statusCode: 400, title: "لوگو باید عکس JPG، PNG یا WebP باشد.");
            var tenantId = me.RequiredMembership.TenantId;
            var relative = Path.Combine(tenantId.ToString("N"), $"logo-{Guid.CreateVersion7():N}.{kind.Ext}");
            var full = Path.Combine(Arte.Api.Cases.AttachmentEndpoints.Root(config, env), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, bytes, ct);
            var t = await db.Tenants.SingleAsync(x => x.Id == tenantId, ct);
            t.LogoPath = relative.Replace('\\', '/');
            audit.Record("settings.logo_changed", t.Id, me.RequiredUserId);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        app.MapDelete("/api/v1/settings/business/logo", async (RequestUser me, ArteDbContext db, Audit audit, CancellationToken ct) =>
        {
            var t = await db.Tenants.SingleAsync(x => x.Id == me.RequiredMembership.TenantId, ct);
            t.LogoPath = null;
            audit.Record("settings.logo_removed", t.Id, me.RequiredUserId);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        app.MapGet("/api/v1/settings/business/logo", async (RequestUser me, ArteDbContext db, IConfiguration config, IHostEnvironment env, CancellationToken ct) =>
        {
            var path = await db.Tenants.AsNoTracking().Where(t => t.Id == me.RequiredMembership.TenantId).Select(t => t.LogoPath).SingleAsync(ct);
            return LogoFile(path, config, env);
        }).RequireAuthorization();

        app.MapPut("/api/v1/settings/business", async (BusinessSettings req, RequestUser me, ArteDbContext db, Audit audit, CancellationToken ct) =>
        {
            // The business type and the vehicles it accepts are set by the platform admin only.
            if (req.BusinessType is not null || req.VehicleKinds is not null)
                return Results.Problem(statusCode: 403, title: "نوع کسب‌وکار و وسایل قابل پذیرش فقط توسط مدیر سامانه تغییر می‌کند.");
            var errors = new Dictionary<string, string[]>();
            if (req.Name is not null && (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 120)) errors["name"] = ["نام ۱ تا ۱۲۰ حرف."];
            if (req.Phone is { Length: > 20 }) errors["phone"] = ["حداکثر ۲۰ کاراکتر."];
            if (req.Address is { Length: > 300 }) errors["address"] = ["حداکثر ۳۰۰ حرف."];
            if (req.BusinessType is not null && !BusinessTypes.All.Contains(req.BusinessType)) errors["businessType"] = ["نوع کسب‌وکار نامعتبر است."];
            if (req.VehicleKinds is { } kinds && (kinds.Length == 0 || kinds.Length > 10 || kinds.Any(k => !Arte.Core.Customers.AssetKinds.All.Contains(k))))
                errors["vehicleKinds"] = ["دست‌کم یک نوع وسیله را انتخاب کنید."];
            if (req.SurveyDelayMinutes is < Arte.Core.Surveys.SurveyRules.MinDelayMinutes or > Arte.Core.Surveys.SurveyRules.MaxDelayMinutes)
                errors["surveyDelayMinutes"] = ["زمان ارسال بین ۵ دقیقه تا ۷۲ ساعت بعد از تحویل."];
            if (req.SurveyAlertBelow is < Arte.Core.Surveys.SurveyRules.MinAlertBelow or > Arte.Core.Surveys.SurveyRules.MaxAlertBelow)
                errors["surveyAlertBelow"] = ["حد رضایت پایین بین ۲ تا ۵ ستاره."];
            if (req.InstagramId is { } ig && ig.Trim().TrimStart('@') is var igName && igName.Length > 0
                && (igName.Length > 30 || !System.Text.RegularExpressions.Regex.IsMatch(igName, @"^[A-Za-z0-9._]+$")))
                errors["instagramId"] = ["آیدی اینستاگرام فقط حرف انگلیسی، عدد، نقطه و _ دارد."];
            foreach (var (field, url, hosts) in new (string, string?, string[])[]
            {
                ("mapNeshanUrl", req.MapNeshanUrl, ["neshan.org", "nshn.ir"]), ("mapGoogleUrl", req.MapGoogleUrl, ["google.com", "goo.gl", "maps.app.goo.gl", "g.co"]),
                ("mapWazeUrl", req.MapWazeUrl, ["waze.com"]), ("mapBaladUrl", req.MapBaladUrl, ["balad.ir"]),
            })
                if (!string.IsNullOrWhiteSpace(url) && !ValidMapUrl(url.Trim(), hosts)) errors[field] = ["لینک معتبر (https) از همین برنامه وارد کنید."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var t = await db.Tenants.SingleAsync(x => x.Id == me.RequiredMembership.TenantId, ct);
            if (req.Name is not null) t.Name = req.Name.Trim();
            if (req.Phone is not null) t.Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
            if (req.Address is not null) t.Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim();
            if (req.RequireAssigneeOnIntake is { } r) t.RequireAssigneeOnIntake = r;
            if (req.RequireCustomerApproval is { } ca) t.RequireCustomerApproval = ca;
            if (req.RequireFinalReview is { } fr) t.RequireFinalReview = fr;
            if (req.BusinessType is { } bt) t.BusinessType = bt;
            if (req.VehicleKinds is { } vk) t.VehicleKinds = vk.Distinct().ToArray();
            if (req.CustomerSmsEnabled is { } sms) t.CustomerSmsEnabled = sms;
            if (req.SmsOnOpened is { } so) t.SmsOnOpened = so;
            if (req.SmsOnReady is { } sr) t.SmsOnReady = sr;
            if (req.SmsOnDelivered is { } sd) t.SmsOnDelivered = sd;
            if (req.TrackShowStages is { } ts) t.TrackShowStages = ts;
            if (req.TrackShowItems is { } ti) t.TrackShowItems = ti;
            if (req.TrackShowAmounts is { } ta) t.TrackShowAmounts = ta;
            if (req.PhotosVisibleByDefault is { } pv) t.PhotosVisibleByDefault = pv;
            // The survey add-on itself (SurveyEnabled) is switched by the platform admin only.
            if (req.SurveySendOn is { } ss) t.SurveySendOn = ss;
            if (req.SurveyDelayMinutes is { } sdm) t.SurveyDelayMinutes = sdm;
            if (req.SurveyAlertBelow is { } sab) t.SurveyAlertBelow = sab;
            if (req.RequireTransferReceipt is { } rr) t.RequireTransferReceipt = rr;
            if (req.RequirePaymentBeforeWork is { } rpw) t.RequirePaymentBeforeWork = rpw;
            if (req.TrackShowPhone is { } tsp) t.TrackShowPhone = tsp;
            if (req.InstagramId is not null) t.InstagramId = string.IsNullOrWhiteSpace(req.InstagramId) ? null : req.InstagramId.Trim().TrimStart('@');
            if (req.MapNeshanUrl is not null) t.MapNeshanUrl = string.IsNullOrWhiteSpace(req.MapNeshanUrl) ? null : req.MapNeshanUrl.Trim();
            if (req.MapGoogleUrl is not null) t.MapGoogleUrl = string.IsNullOrWhiteSpace(req.MapGoogleUrl) ? null : req.MapGoogleUrl.Trim();
            if (req.MapWazeUrl is not null) t.MapWazeUrl = string.IsNullOrWhiteSpace(req.MapWazeUrl) ? null : req.MapWazeUrl.Trim();
            if (req.MapBaladUrl is not null) t.MapBaladUrl = string.IsNullOrWhiteSpace(req.MapBaladUrl) ? null : req.MapBaladUrl.Trim();
            if (req.SmsOnWaitingPart is { } swp) t.SmsOnWaitingPart = swp;
            if (req.SmsToMasterOnFinished is { } smf) t.SmsToMasterOnFinished = smf;
            audit.Record("settings.business_updated", t.Id, me.RequiredUserId);
            await db.SaveChangesAsync(ct);
            // Optional workflow steps follow the settings.
            await WorkflowUpgrader.UpgradeTenantAsync(db, t, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                t.Name, t.Phone, t.Address, t.RequireAssigneeOnIntake, t.RequireCustomerApproval, t.RequireFinalReview,
                t.BusinessType, t.VehicleKinds, t.CustomerSmsEnabled, t.SmsOnOpened, t.SmsOnReady, t.SmsOnDelivered,
                t.TrackShowStages, t.TrackShowItems, t.TrackShowAmounts, t.PhotosVisibleByDefault,
                    t.SurveyEnabled, t.SurveySendOn, t.SurveyDelayMinutes, t.SurveyAlertBelow, t.RequireTransferReceipt, t.RequirePaymentBeforeWork, t.TrackShowPhone, t.InstagramId, t.MapNeshanUrl, t.MapGoogleUrl, t.MapWazeUrl, t.MapBaladUrl, t.SmsOnWaitingPart, t.SmsToMasterOnFinished,
            });
        }).RequirePermission(Permissions.SettingsManage);

        app.MapPost("/api/v1/tenants", async (CreateTenant req, RequestUser me, IServiceScopeFactory scopes,
            IOptions<SignupOptions> signup, ArteDbContext db, CancellationToken ct) =>
        {
            if (signup.Value.RequireInviteCode && !InviteCodeMatches(signup.Value.InviteCode, req.InviteCode))
                return Results.Problem(statusCode: 403, title: "کد دعوت معتبر نیست.");

            var errors = new Dictionary<string, string[]>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == me.RequiredUserId, ct);
            var ownerName = req.OwnerName?.Trim();
            if (string.IsNullOrEmpty(ownerName)) ownerName = user.DisplayName;
            if (string.IsNullOrEmpty(ownerName) || ownerName.Length < 2) errors["ownerName"] = ["نام و نام خانوادگی را بنویسید."];
            else if (ownerName.Length > 80) errors["ownerName"] = ["نام حداکثر ۸۰ حرف."];
            // The business name is optional at sign-up; it can be changed later in Settings.
            var name = req.Name?.Trim();
            // Empty → named after the owner's family name (the last word of the full name); editable in Settings.
            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(ownerName))
                name = $"{req.BusinessType switch { BusinessTypes.MotorcycleRepair => "موتورسازی", BusinessTypes.QuickService => "آپاراتی", _ => "تعمیرگاه" }} {ownerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]}";
            if (string.IsNullOrEmpty(name) || name.Length > 120) errors["name"] = ["نام کسب‌وکار حداکثر ۱۲۰ حرف."];
            string? phone = null;
            if (!string.IsNullOrWhiteSpace(req.Phone))
            {
                phone = req.Phone.Trim();
                if (phone.Length > 20 || !phone.All(c => char.IsAsciiDigit(c) || c is '-' or ' ' or '+'))
                    errors["phone"] = ["شماره تلفن معتبر نیست."];
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            // No phone given: the owner's mobile is the business phone (shown to customers; editable in Settings).
            phone ??= user.Mobile;

            // Sign-up makes the first business only; further branches are created by Arte (admin panel).
            if (await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.UserId == me.RequiredUserId && m.IsActive && !m.IsSupport, ct))
                return Results.Problem(statusCode: 403, title: "ساخت شعبه‌ی جدید با پشتیبانی آرته است.");

            var businessType = req.BusinessType ?? BusinessTypes.MotorcycleRepair;
            if (!BusinessTypes.All.Contains(businessType))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["businessType"] = ["نوع کسب‌وکار را انتخاب کنید."] });
            var tenant = await ProvisionAsync(scopes, me.RequiredUserId, name!, phone, ownerName, ct, businessType);

            return Results.Created($"/api/v1/tenants/{tenant.Id}", new { tenant.Id, tenant.Name, TrialDays = Arte.Core.Licensing.LicensePolicy.TrialDays });
        }).RequireAuthorization().RequireRateLimiting("auth");
    }

    /// <summary>Creates a business with the motorcycle workflow and makes the user its owner.</summary>
    public static async Task<Tenant> ProvisionAsync(IServiceScopeFactory scopes, Guid ownerUserId, string name,
        string? phone, string? ownerName, CancellationToken ct, string businessType = BusinessTypes.MotorcycleRepair, Guid? organizationId = null)
    {
        // A fresh scope so the new tenant becomes the tenant of this unit of work only.
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var clock = sp.GetRequiredService<IClock>();
        var db = sp.GetRequiredService<ArteDbContext>();
        var tenant = new Tenant
        {
            Name = name, Phone = phone, Vertical = WorkflowTemplates.MotorcycleRepair, CreatedAt = clock.UtcNow,
            BusinessType = businessType, VehicleKinds = BusinessTypes.DefaultVehicleKinds(businessType),
            OrganizationId = organizationId,
            // The free 14-day licence includes the customer survey; the platform admin can still switch it.
            SurveyEnabled = true,
        };
        sp.GetRequiredService<TenantContext>().Set(tenant.Id);

        db.Tenants.Add(tenant);
        // The intro tour opens by itself only for a person's very first business (their sign-up). Every later
        // membership starts as «tour done»; the tour stays available from Settings → راهنمای برنامه.
        var firstEver = !await db.Memberships.IgnoreQueryFilters([ArteDbContext.TenantFilter]).AnyAsync(m => m.UserId == ownerUserId, ct);
        var owner = new Membership
        {
            TourDoneAt = firstEver ? null : clock.UtcNow,
            TenantId = tenant.Id,
            UserId = ownerUserId,
            Role = Roles.Owner,
            DisplayName = string.IsNullOrEmpty(ownerName) ? null : ownerName,
            Permissions = Roles.DefaultPermissions(Roles.Owner),
            CreatedAt = clock.UtcNow,
            SurveyNotify = true,
        };
        db.Memberships.Add(owner);
        db.Licenses.Add(Arte.Api.Licensing.LicenseService.Trial(tenant.Id, clock.UtcNow));
        db.Workflows.Add(WorkflowTemplates.Instantiate(WorkflowTemplates.MotorcycleRepair, tenant.Id, tenant));

        if (!string.IsNullOrEmpty(ownerName))
        {
            var user = await db.Users.SingleAsync(u => u.Id == ownerUserId, ct);
            // The user's own default name is set only once; a rename in one branch never rewrites another branch's name.
            user.DisplayName ??= ownerName;
        }
        sp.GetRequiredService<Audit>().Record("tenant.created", tenant.Id, ownerUserId, tenant.Name);
        await db.SaveChangesAsync(ct);

        // Every new business starts with the sample case the intro tour walks through.
        if (sp.GetRequiredService<IConfiguration>().GetValue("Onboarding:SampleData", true))
            await Arte.Api.Onboarding.OnboardingEndpoints.EnsureSampleAsync(db, owner, clock, ct);
        return tenant;
    }

    private static bool InviteCodeMatches(string expected, string? presented)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(presented.Trim()));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
