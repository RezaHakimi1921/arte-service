using Arte.Api.Onboarding;
using Arte.Api.Diagnostics;
using System.Threading.RateLimiting;
using Arte.Api.Account;
using Arte.Api.Auth;
using Arte.Api.Billing;
using Arte.Api.Cases;
using Arte.Api.Customers;
using Arte.Api.Options;
using Arte.Api.Reports;
using Arte.Api.Security;
using Arte.Api.Sms;
using Arte.Api.Staff;
using Arte.Api.Tenants;
using Arte.Api.Workflows;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Arte.Core.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
});

// Error tracking (Bugsink, Sentry protocol) only when a DSN is configured. No request bodies, cookies or
// user data are sent, and SMS logs (which hold codes in development) never become breadcrumbs.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"]))
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = builder.Configuration["Sentry:Dsn"];
        o.Environment = builder.Environment.EnvironmentName.ToLowerInvariant();
        o.SendDefaultPii = false;
        o.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
        o.MinimumEventLevel = LogLevel.Error;
        o.SetBeforeBreadcrumb(b => b.Category?.Contains("Sms", StringComparison.OrdinalIgnoreCase) == true ? null : b);
    });
}

var config = builder.Configuration;
builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
builder.Services.Configure<OtpOptions>(config.GetSection(OtpOptions.Section));
builder.Services.Configure<SignupOptions>(config.GetSection(SignupOptions.Section));
builder.Services.Configure<SmsOptions>(config.GetSection(SmsOptions.Section));

var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
var otp = config.GetSection(OtpOptions.Section).Get<OtpOptions>() ?? new OtpOptions();
var smsOptions = config.GetSection(SmsOptions.Section).Get<SmsOptions>() ?? new SmsOptions();
StartupChecks.Validate(builder.Environment, jwt, otp, smsOptions, config.GetConnectionString("Default"));

builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddScoped<RequestUser>();
builder.Services.AddDbContext<ArteDbContext>(o => o.UseNpgsql(config.GetConnectionString("Default")));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Audit>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<Arte.Api.Tracking.CustomerNotifier>();
builder.Services.AddScoped<Arte.Api.Surveys.SurveyService>();
builder.Services.AddSingleton<Arte.Api.Surveys.SurveySender>();
// Tests switch the timer off and run the sender by hand.
if (config.GetValue("Surveys:SenderEnabled", true))
    builder.Services.AddHostedService(sp => sp.GetRequiredService<Arte.Api.Surveys.SurveySender>());
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<FakeSmsProvider>();
if (smsOptions.Provider == "smsir")
{
    builder.Services.AddHttpClient<ISmsProvider, SmsIrProvider>(SmsIrProvider.HttpClientName, c =>
    {
        c.BaseAddress = new Uri(smsOptions.SmsIr.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(15);
        c.DefaultRequestHeaders.Add("x-api-key", smsOptions.SmsIr.ApiKey);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    });
}
else if (smsOptions.Provider == "iranpayamak")
{
    builder.Services.AddHttpClient<ISmsProvider, IranPayamakProvider>(c =>
    {
        c.BaseAddress = new Uri(smsOptions.IranPayamak.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(15);
        c.DefaultRequestHeaders.Add("Api-Key", smsOptions.IranPayamak.ApiKey);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    });
}
else
{
    builder.Services.AddSingleton<ISmsProvider>(sp => sp.GetRequiredService<FakeSmsProvider>());
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ArteClaims.UserId,
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:GlobalPerMinute", 300), Window = TimeSpan.FromMinutes(1),
        }));
    o.AddPolicy("auth", http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1),
        }));
    // Token refresh, switching business, logout: not credential guessing, so a looser limit;
    // reloads and several open tabs must not log people out.
    o.AddPolicy("session", http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:SessionPerMinute", 60), Window = TimeSpan.FromMinutes(1),
        }));
});

// The API is only reachable through the reverse proxy on the internal Docker network.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler(e => e.Run(async http =>
{
    var error = http.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Errors");
    switch (error)
    {
        case TenantViolationException:
            logger.LogCritical(error, "Tenant isolation violation on {Path}", http.Request.Path);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            break;
        case BadHttpRequestException bad:
            http.Response.StatusCode = bad.StatusCode;
            break;
        default:
            logger.LogError(error, "Unhandled error on {Path}", http.Request.Path);
            http.Response.StatusCode = StatusCodes.Status500InternalServerError;
            break;
    }
    // Never echo exception details to the client.
    await Results.Problem(statusCode: http.Response.StatusCode, title: "درخواست انجام نشد.").ExecuteAsync(http);
}));
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<Arte.Api.Licensing.LicenseEnforcementMiddleware>();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).DisableRateLimiting();
app.MapAuth();
app.MapOpenMode();
app.MapAccount();
app.MapTenants();
app.MapStaff();
app.MapCustomers();
app.MapWorkflows();
app.MapCases();
app.MapBilling();
Arte.Api.Billing.StandardCatalogEndpoints.MapStandardCatalog(app);
app.MapAttachments();
app.MapClientErrors();
app.MapOnboarding();
Arte.Api.Tracking.TrackingEndpoints.MapTracking(app);
Arte.Api.Surveys.SurveyEndpoints.MapSurveys(app);
// Public, for the sign-in and tracking pages: how to reach Arte support.
app.MapGet("/api/v1/public/info", (IConfiguration c) => Results.Ok(new { SupportPhone = c["Platform:SupportPhone"] })).AllowAnonymous();
Arte.Api.Licensing.LicenseEndpoints.MapLicensing(app);
app.MapReports();

if (config.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Database.MigrateAsync();
}

// Keep every business on the current workflow template (idempotent).
{
    await using var scope = app.Services.CreateAsyncScope();
    var tenantIds = await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Tenants.Select(t => t.Id).ToListAsync();
    foreach (var tenantId in tenantIds)
    {
        await using var tenantScope = app.Services.CreateAsyncScope();
        tenantScope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
        var db = tenantScope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
        if (await Arte.Core.Workflows.WorkflowUpgrader.UpgradeTenantAsync(db, tenant, CancellationToken.None) > 0)
        {
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Workflow upgraded for tenant {TenantId}", tenantId);
        }
    }
}

// Price list once, and a one-time trial for businesses created before licences existed.
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
    var now = scope.ServiceProvider.GetRequiredService<Arte.Core.Common.IClock>().UtcNow;
    await Arte.Api.Licensing.LicenseService.SeedPlansAsync(db, now, CancellationToken.None);
    var backfilled = await Arte.Api.Licensing.LicenseService.BackfillAsync(db, now, CancellationToken.None);
    if (backfilled > 0) app.Logger.LogInformation("Granted a starting trial to {Count} existing businesses", backfilled);
}

if (!config.GetValue("Auth:OpenMode", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var revoked = await scope.ServiceProvider.GetRequiredService<TokenService>().RevokeOpenModeSessionsAsync(CancellationToken.None);
    if (revoked > 0) app.Logger.LogWarning("Open mode is off: revoked {Count} open-mode sessions", revoked);
}
else
{
    app.Logger.LogWarning("OPEN MODE IS ON: anyone who opens the site acts as the business owner.");
}

if (args.Length > 0 && args[0] == "set-password")
    return await SetPasswordCommand.RunAsync(app.Services, args);
if (args.Length > 0 && args[0] == "set-admin")
    return await SetAdminCommand.RunAsync(app.Services, args);

app.Run();
return 0;

static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

public partial class Program;

static class StartupChecks
{
    /// <summary>Refuses to start with missing or weak secrets.</summary>
    public static void Validate(IHostEnvironment env, JwtOptions jwt, OtpOptions otp, SmsOptions sms, string? connectionString)
    {
        var problems = new List<string>();
        byte[] key = [];
        try { key = Convert.FromBase64String(jwt.Key); } catch (FormatException) { }
        if (key.Length < 32) problems.Add("Jwt:Key must be base64 of at least 32 random bytes.");
        if (otp.Pepper.Length < 32) problems.Add("Otp:Pepper must be at least 32 characters.");
        if (string.IsNullOrEmpty(connectionString)) problems.Add("ConnectionStrings:Default is missing.");
        if (env.IsProduction() && sms.Provider == "fake" && !sms.AllowFakeInProduction)
            problems.Add("Sms:Provider is 'fake' in production. Set Sms:AllowFakeInProduction with Sms:FakeAllowedMobiles, or configure a real provider.");
        if (sms.Provider is not ("fake" or "smsir" or "iranpayamak")) problems.Add($"Sms:Provider '{sms.Provider}' is unknown (use fake, smsir or iranpayamak).");
        if (sms.Provider == "iranpayamak")
        {
            var ip = sms.IranPayamak;
            if (ip.ApiKey.Length < 10) problems.Add("Sms:IranPayamak:ApiKey is missing.");
            if (ip.LineNumber.Length == 0 || !ip.LineNumber.All(char.IsAsciiDigit)) problems.Add("Sms:IranPayamak:LineNumber (digits) is missing.");
            if (!ip.Templates.TryGetValue("auth_otp", out var otpPattern) || string.IsNullOrWhiteSpace(otpPattern))
                problems.Add("Sms:IranPayamak:Templates:auth_otp (pattern code) is missing.");
        }
        if (sms.Provider == "smsir" && sms.SmsIr.ApiKey.Length < 20) problems.Add("Sms:SmsIr:ApiKey is missing.");
        if (sms.Provider == "smsir" && (!sms.SmsIr.Templates.TryGetValue("auth_otp", out var otpTemplate) || otpTemplate <= 0)) problems.Add("Sms:SmsIr:Templates:auth_otp (template id) is missing.");

        if (problems.Count > 0)
            throw new InvalidOperationException("Configuration is not safe to start:\n- " + string.Join("\n- ", problems));
    }
}
