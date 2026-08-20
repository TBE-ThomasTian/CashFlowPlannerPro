using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Services;

public static class SevDeskSecureStore
{
    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");

    private static readonly string StoreFile = Path.Combine(StoreDir, "sevdesk.enc");
    private static readonly string EntropyFile = Path.Combine(StoreDir, "sevdesk.key");

    public static bool Save(SevDeskSecureData data)
        => Save(data, StoreFile, out _);

    private static bool Save(SevDeskSecureData data, string storeFile, out string? errorReference)
    {
        errorReference = null;
        try
        {
            Directory.CreateDirectory(StoreDir);
            var entropy = GetOrCreateEntropy();
            var json = JsonSerializer.Serialize(data);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = ProtectedData.Protect(
                plainBytes,
                entropy,
                DataProtectionScope.CurrentUser);

            WriteStoreAtomically(encryptedBytes, storeFile);
            return true;
        }
        catch (Exception ex)
        {
            errorReference = AppLogger.LogException(
                "sevdesk.secret_store.save_failed",
                ex,
                new { scope = string.Equals(storeFile, StoreFile, StringComparison.OrdinalIgnoreCase) ? "legacy" : "database" });
            return false;
        }
    }

    private static void WriteStoreAtomically(byte[] encryptedBytes, string storeFile)
    {
        var tempFile = Path.Combine(
            StoreDir,
            $".{Path.GetFileName(storeFile)}.{Guid.NewGuid():N}.tmp");
        var backupFile = Path.Combine(
            StoreDir,
            $".{Path.GetFileName(storeFile)}.{Guid.NewGuid():N}.bak");
        FileAttributes? originalAttributes = null;

        try
        {
            File.WriteAllBytes(tempFile, encryptedBytes);
            if (File.Exists(storeFile))
            {
                originalAttributes = File.GetAttributes(storeFile);
                if ((originalAttributes.Value & FileAttributes.Hidden) != 0)
                {
                    File.SetAttributes(
                        storeFile,
                        originalAttributes.Value & ~FileAttributes.Hidden);
                }

                File.Replace(tempFile, storeFile, backupFile);
            }
            else
            {
                File.Move(tempFile, storeFile);
            }

            File.SetAttributes(storeFile, File.GetAttributes(storeFile) | FileAttributes.Hidden);
        }
        catch
        {
            if (originalAttributes.HasValue)
            {
                try
                {
                    if (!File.Exists(storeFile) && File.Exists(backupFile))
                        File.Move(backupFile, storeFile);

                    if (File.Exists(storeFile))
                        File.SetAttributes(storeFile, originalAttributes.Value);
                }
                catch { }
            }

            throw;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); }
                catch { }
            }

            if (File.Exists(backupFile) && File.Exists(storeFile))
            {
                try { File.Delete(backupFile); }
                catch { }
            }
        }
    }

    public static bool SaveForCurrentDatabase(string apiToken)
        => SaveForCurrentDatabase(apiToken, out _);

    public static bool SaveForCurrentDatabase(string apiToken, out string? errorReference)
    {
        errorReference = null;
        if (string.IsNullOrWhiteSpace(apiToken))
            return false;

        var data = new SevDeskSecureData
        {
            ApiToken = apiToken.Trim(),
            DatabaseInstanceId = Database.Instance.GetDatabaseInstanceId()
        };

        // Keep the legacy file as a bound record so older installations can
        // migrate explicitly only once. The actual current implementation
        // stores one encrypted token per logical database instance.
        if (!Save(data, StoreFile, out errorReference))
            return false;

        return Save(data, GetScopedStoreFile(data.DatabaseInstanceId), out errorReference);
    }

    public static SevDeskSecureData? Load()
        => Load(StoreFile);

    private static SevDeskSecureData? Load(string storeFile)
    {
        try
        {
            if (!File.Exists(storeFile) || !File.Exists(EntropyFile))
                return null;

            var entropy = File.ReadAllBytes(EntropyFile);
            var encryptedBytes = File.ReadAllBytes(storeFile);
            var plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                entropy,
                DataProtectionScope.CurrentUser);

            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<SevDeskSecureData>(json);
        }
        catch (Exception ex)
        {
            AppLogger.LogException(
                "sevdesk.secret_store.load_failed",
                ex,
                new { scope = string.Equals(storeFile, StoreFile, StringComparison.OrdinalIgnoreCase) ? "legacy" : "database" });
            return null;
        }
    }

    public static SevDeskSecureData? LoadForCurrentDatabase()
    {
        var currentDatabaseId = Database.Instance.GetDatabaseInstanceId();
        var data = Load(GetScopedStoreFile(currentDatabaseId));
        if (data == null || string.IsNullOrWhiteSpace(data.DatabaseInstanceId))
            return null;

        return string.Equals(
                data.DatabaseInstanceId.Trim(),
                currentDatabaseId,
                StringComparison.OrdinalIgnoreCase)
            ? data
            : null;
    }

    private static string GetScopedStoreFile(string databaseInstanceId)
    {
        if (!Guid.TryParseExact(databaseInstanceId?.Trim(), "N", out var parsed))
            throw new InvalidOperationException("Die Datenbankkennung ist ungültig.");

        return Path.Combine(StoreDir, $"sevdesk-{parsed:N}.enc");
    }

    private static byte[] GetOrCreateEntropy()
    {
        if (File.Exists(EntropyFile))
            return File.ReadAllBytes(EntropyFile);

        var entropy = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(entropy);
        File.WriteAllBytes(EntropyFile, entropy);
        File.SetAttributes(EntropyFile, FileAttributes.Hidden);
        return entropy;
    }
}
