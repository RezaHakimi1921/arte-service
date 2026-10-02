using System.Security.Cryptography;

namespace Arte.Core.Identity;

/// <summary>PBKDF2-SHA256, 600k iterations, 16-byte salt. Format: pbkdf2-sha256$iterations$salt$hash (base64).</summary>
public static class PasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public const int MinLength = 10;

    /// <summary>A real hash of a random password, verified against when the username does not exist,
    /// so response time does not reveal which usernames exist.</summary>
    public static readonly string Dummy = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations)) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
