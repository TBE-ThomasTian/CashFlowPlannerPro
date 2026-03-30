using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Services;

public static class SevDeskSecureStore
{
    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");

    private static readonly string StoreFile = Path.Combine(StoreDir, "sevdesk.enc");
    private static readonly string EntropyFile = Path.Combine(StoreDir, "sevdesk.key");

    public static void Save(SevDeskSecureData data)
    {
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

            File.WriteAllBytes(StoreFile, encryptedBytes);
            File.SetAttributes(StoreFile, FileAttributes.Hidden);
        }
        catch
        {
        }
    }

    public static SevDeskSecureData? Load()
    {
        try
        {
            if (!File.Exists(StoreFile) || !File.Exists(EntropyFile))
                return null;

            var entropy = File.ReadAllBytes(EntropyFile);
            var encryptedBytes = File.ReadAllBytes(StoreFile);
            var plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                entropy,
                DataProtectionScope.CurrentUser);

            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<SevDeskSecureData>(json);
        }
        catch
        {
            return null;
        }
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
