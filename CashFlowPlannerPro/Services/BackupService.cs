using System.IO;
using CashFlowPlannerPro.Data;

namespace CashFlowPlannerPro.Services;

public static class BackupService
{
    public static bool SupportsFileBackup()
    {
        var config = App.CurrentConnectionConfig;
        return config?.Backend == DatabaseBackend.SQLite
               && !string.IsNullOrWhiteSpace(config.FilePath);
    }

    public static void CreateBackup(string targetPath)
    {
        var config = RequireSqliteConfig();
        var sourcePath = config.FilePath;

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Quelle und Ziel duerfen nicht identisch sein.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        Database.Instance.Close();
        try
        {
            if (File.Exists(targetPath))
                File.Delete(targetPath);

            File.Copy(sourcePath, targetPath, true);
        }
        finally
        {
            Reopen(config);
        }
    }

    public static void RestoreBackup(string backupPath)
    {
        var config = RequireSqliteConfig();
        var targetPath = config.FilePath;

        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup-Datei wurde nicht gefunden.", backupPath);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var emergencyBackup = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $"{Path.GetFileNameWithoutExtension(targetPath)}-pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        Database.Instance.Close();

        File.Copy(targetPath, emergencyBackup, true);
        DeleteIfExists(targetPath);
        DeleteIfExists(targetPath + "-wal");
        DeleteIfExists(targetPath + "-shm");
        File.Copy(backupPath, targetPath, true);

        Reopen(config);
    }

    private static ConnectionConfig RequireSqliteConfig()
    {
        var config = App.CurrentConnectionConfig?.Clone();
        if (config?.Backend != DatabaseBackend.SQLite || string.IsNullOrWhiteSpace(config.FilePath))
            throw new InvalidOperationException("Backup/Restore wird aktuell nur fuer lokale SQLite-Datenbanken unterstuetzt.");
        return config;
    }

    private static void Reopen(ConnectionConfig config)
    {
        Database.Instance.Open(config);
        Database.Instance.EnsureSchema();
        App.CurrentConnectionConfig = config.Clone();
        App.DatabasePath = config.FilePath;
        App.CurrentUserId = Database.Instance.GetUserId(App.CurrentUsername);
        App.LoadPermissions();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
