using System.Text;
using System.Security.Cryptography;

namespace CashFlowPlannerPro.Services;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int LegacyIterations = 100_000;
    private const int Iterations = 600_000;
    private const string FormatName = "pbkdf2-sha256";
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Creates a PBKDF2 hash with random salt.
    /// Format: pbkdf2-sha256$iterations$base64(salt)$base64(hash)
    /// </summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return $"{FormatName}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a password against a stored hash.
    /// Also supports legacy plaintext passwords for migration.
    /// </summary>
    public static bool Verify(string password, string storedHash)
    {
        try
        {
            if (TryParseCurrentFormat(storedHash, out var iterations, out var salt, out var expectedHash))
            {
                var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expectedHash.Length);
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }

            var legacyParts = storedHash.Split(':');
            if (legacyParts.Length == 2)
            {
                var legacySalt = Convert.FromBase64String(legacyParts[0]);
                var legacyExpectedHash = Convert.FromBase64String(legacyParts[1]);
                var legacyActualHash = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    legacySalt,
                    LegacyIterations,
                    Algorithm,
                    legacyExpectedHash.Length);
                return CryptographicOperations.FixedTimeEquals(legacyActualHash, legacyExpectedHash);
            }

            var suppliedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            var storedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(storedHash));
            return CryptographicOperations.FixedTimeEquals(suppliedDigest, storedDigest);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a stored hash is in the legacy plaintext format.
    /// </summary>
    public static bool IsLegacyFormat(string storedHash) => NeedsRehash(storedHash);

    public static bool NeedsRehash(string storedHash)
        => !TryParseCurrentFormat(storedHash, out var iterations, out _, out var hash) ||
           iterations < Iterations ||
           hash.Length != HashSize;

    private static bool TryParseCurrentFormat(
        string storedHash,
        out int iterations,
        out byte[] salt,
        out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        if (storedHash.Length > 512)
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], FormatName, StringComparison.Ordinal) ||
            !int.TryParse(parts[1], out iterations) ||
            iterations is < LegacyIterations or > 2_000_000)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
            return salt.Length == SaltSize && hash.Length == HashSize;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
