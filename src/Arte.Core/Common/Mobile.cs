using System.Text;
using System.Text.RegularExpressions;

namespace Arte.Core.Common;

/// <summary>Iranian mobile numbers, normalized to the 09xxxxxxxxx form.</summary>
public static partial class Mobile
{
    [GeneratedRegex(@"^09\d{9}$")]
    private static partial Regex Canonical();

    public static bool TryNormalize(string? input, out string mobile)
    {
        mobile = "";
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32) return false;

        var sb = new StringBuilder(16);
        foreach (var ch in input)
        {
            if (ch is >= '0' and <= '9') sb.Append(ch);
            else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));
            else if (ch is ' ' or '-' or '(' or ')' or '+') continue;
            else return false;
        }

        var digits = sb.ToString();
        if (digits.StartsWith("0098")) digits = "0" + digits[4..];
        else if (digits.StartsWith("98") && digits.Length == 12) digits = "0" + digits[2..];
        else if (digits.StartsWith('9') && digits.Length == 10) digits = "0" + digits;

        if (!Canonical().IsMatch(digits)) return false;
        mobile = digits;
        return true;
    }

    /// <summary>For logs: 0912***4567.</summary>
    public static string Mask(string mobile) =>
        mobile.Length == 11 ? $"{mobile[..4]}***{mobile[7..]}" : "***";

    /// <summary>
    /// Iranian mobile as a positive long without the leading zero (SmartX recipientId):
    /// 09362660550 / 989362660550 → 9362660550.
    /// </summary>
    public static bool TryToNationalLong(string? input, out long national)
    {
        national = 0;
        if (!TryNormalize(input, out var local)) return false;
        return long.TryParse(local.AsSpan(1), out national) && national > 0;
    }
}
