using System.Security.Cryptography;

namespace CashFlowPlannerPro.Services;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Creates a PBKDF2 hash with random salt.
    /// Format: base64(salt):base64(hash)
    /// </summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a password against a stored hash.
    /// Also supports legacy plaintext passwords for migration.
    /// </summary>
    public static bool Verify(string password, string storedHash)
    {
        // Legacy plaintext check (for migration from old format)
        if (!storedHash.Contains(':'))
            return password == storedHash;

        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a stored hash is in the legacy plaintext format.
    /// </summary>
    public static bool IsLegacyFormat(string storedHash) => !storedHash.Contains(':');
}
