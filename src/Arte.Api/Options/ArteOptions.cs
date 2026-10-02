namespace Arte.Api.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "arte-service";
    public string Audience { get; set; } = "arte-service";
    /// <summary>Base64, at least 32 bytes. From the environment, never from appsettings in production.</summary>
    public string Key { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    /// <summary>A just-rotated refresh token is accepted again within this window (lost response), not treated as theft.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}

public sealed class OtpOptions
{
    public const string Section = "Otp";
    /// <summary>Server secret mixed into code hashes, so a leaked database does not reveal live codes.</summary>
    public string Pepper { get; set; } = "";
    public int Digits { get; set; } = 6;
    public int TtlSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxPerMobilePerHour { get; set; } = 5;
    public int MaxPerIpPerHour { get; set; } = 20;
}

public sealed class SignupOptions
{
    public const string Section = "Signup";
    /// <summary>While in pilot, creating a business needs this code. Empty disables sign-up.</summary>
    public string InviteCode { get; set; } = "";
    public int MaxBusinessesPerUser { get; set; } = 3;
}

public sealed class SmsOptions
{
    public const string Section = "Sms";
    /// <summary>"fake" until a provider is chosen.</summary>
    public string Provider { get; set; } = "fake";
    /// <summary>
    /// The fake provider only writes codes to the server log. In production it must be switched on explicitly,
    /// and then only the mobiles listed here can request a code.
    /// </summary>
    public bool AllowFakeInProduction { get; set; }
    public string[] FakeAllowedMobiles { get; set; } = [];
}
