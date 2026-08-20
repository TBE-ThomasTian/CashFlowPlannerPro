using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Data;

/// <summary>
/// Stores MariaDB connection settings with Windows DPAPI protection scoped to
/// the current Windows user.
/// </summary>
public static class SecureConnectionStore
{
    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");

    private static readonly string StoreFile = Path.Combine(StoreDir, "connection.enc");
    private static readonly string EntropyFile = Path.Combine(StoreDir, "connection.key");

    /// <summary>
    /// Saves connection settings encrypted to disk.
    /// Uses Windows DPAPI (DataProtectionScope.CurrentUser) so only
    /// the current Windows user on this machine can decrypt.
    /// </summary>
    public static bool Save(SecureConnectionData data)
    {
        try
        {
            Directory.CreateDirectory(StoreDir);

            // Generate random entropy (salt) if it doesn't exist
            byte[] entropy;
            if (File.Exists(EntropyFile))
            {
                entropy = File.ReadAllBytes(EntropyFile);
            }
            else
            {
                entropy = new byte[32];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(entropy);
                File.WriteAllBytes(EntropyFile, entropy);
                // Restrict file permissions — mark as hidden
                File.SetAttributes(EntropyFile, FileAttributes.Hidden);
            }

            // Serialize to JSON
            var json = JsonSerializer.Serialize(data);
            var plainBytes = Encoding.UTF8.GetBytes(json);

            // Encrypt with DPAPI — only this Windows user can decrypt
            var encryptedBytes = ProtectedData.Protect(
                plainBytes,
                entropy,
                DataProtectionScope.CurrentUser);

            WriteStoreAtomically(encryptedBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteStoreAtomically(byte[] encryptedBytes)
    {
        var tempFile = Path.Combine(StoreDir, $".connection.{Guid.NewGuid():N}.tmp");
        var backupFile = Path.Combine(StoreDir, $".connection.{Guid.NewGuid():N}.bak");
        FileAttributes? originalAttributes = null;

        try
        {
            File.WriteAllBytes(tempFile, encryptedBytes);
            if (File.Exists(StoreFile))
            {
                originalAttributes = File.GetAttributes(StoreFile);
                var writableAttributes = originalAttributes.Value &
                                         ~FileAttributes.Hidden &
                                         ~FileAttributes.ReadOnly;
                File.SetAttributes(StoreFile, writableAttributes);
                File.Replace(tempFile, StoreFile, backupFile);
            }
            else
            {
                File.Move(tempFile, StoreFile);
            }

            File.SetAttributes(StoreFile, File.GetAttributes(StoreFile) | FileAttributes.Hidden);
        }
        catch
        {
            try
            {
                if (!File.Exists(StoreFile) && File.Exists(backupFile))
                    File.Move(backupFile, StoreFile);

                if (originalAttributes.HasValue && File.Exists(StoreFile))
                    File.SetAttributes(StoreFile, originalAttributes.Value);
            }
            catch { }

            throw;
        }
        finally
        {
            TryDeleteTemporaryFile(tempFile);
            if (File.Exists(StoreFile))
                TryDeleteTemporaryFile(backupFile);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var attributes = File.GetAttributes(path) & ~FileAttributes.ReadOnly & ~FileAttributes.Hidden;
            File.SetAttributes(path, attributes);
            File.Delete(path);
        }
        catch { }
    }

    /// <summary>
    /// Loads and decrypts saved connection settings.
    /// Returns null if no saved data exists or decryption fails.
    /// </summary>
    public static SecureConnectionData? Load()
    {
        try
        {
            if (!File.Exists(StoreFile) || !File.Exists(EntropyFile))
                return null;

            var entropy = File.ReadAllBytes(EntropyFile);
            var encryptedBytes = File.ReadAllBytes(StoreFile);

            // Decrypt with DPAPI
            var plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                entropy,
                DataProtectionScope.CurrentUser);

            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<SecureConnectionData>(json);
        }
        catch (Exception ex)
        {
            AppLogger.LogException("connection_settings.load_failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Deletes all saved connection data from disk.
    /// </summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(StoreFile)) File.Delete(StoreFile);
            if (File.Exists(EntropyFile)) File.Delete(EntropyFile);
        }
        catch (Exception ex)
        {
            AppLogger.LogException("connection_settings.delete_failed", ex);
        }
    }

    /// <summary>
    /// Returns true if saved credentials exist.
    /// </summary>
    public static bool Exists() => File.Exists(StoreFile) && File.Exists(EntropyFile);
}

/// <summary>
/// Data model for encrypted connection settings.
/// All fields are encrypted together as one block.
/// </summary>
public class SecureConnectionData
{
    public string? Host { get; set; }
    public int Port { get; set; } = 3306;
    public string? DatabaseName { get; set; }
    public string? DbUsername { get; set; }
    public string? DbPassword { get; set; }

    // Login credentials
    public string? AppUsername { get; set; }
    public bool RememberSettings { get; set; }
}
