using Arte.Core.Identity;

namespace Arte.Api.Auth;

/// <summary>Sign-in name and password rules shared by every place a manager or the person themself sets them.</summary>
public static class CredentialRules
{
    public static string? NormalizeUsername(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();

    /// <summary>Adds Persian messages to <paramref name="errors"/> under "username" / "password"; the mobile number is the other sign-in name.</summary>
    public static void Validate(Dictionary<string, string[]> errors, string? username, string? password, string mobile)
    {
        if (username is not null && (username.Length is < 3 or > 40
            || !username.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-')))
            errors["username"] = ["نام کاربری ۳ تا ۴۰ حرف انگلیسی، عدد، نقطه، خط تیره یا زیرخط."];

        if (password is null) return;
        if (password.Length < PasswordHasher.MinLength || password.Length > 200)
            errors["password"] = [$"رمز باید حداقل {PasswordHasher.MinLength} کاراکتر باشد."];
        else if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase) || password == mobile)
            errors["password"] = ["رمز نباید با شماره موبایل یا نام کاربری یکی باشد."];
    }
}
