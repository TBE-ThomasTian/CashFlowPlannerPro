using System.IO;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using Microsoft.Data.Sqlite;

namespace CashFlowPlannerPro.Services;

public static class BackupService
{
    private static readonly object OperationGate = new();

    public static bool SupportsFileBackup()
    {
        var config = App.CurrentConnectionConfig;
        return config?.Backend == DatabaseBackend.SQLite
               && !string.IsNullOrWhiteSpace(config.FilePath);
    }

    public static void CreateBackup(string targetPath)
    {
        lock (OperationGate)
            CreateBackupCore(targetPath);
    }

    private static void CreateBackupCore(string targetPath)
    {
        var config = RequireSqliteConfig();
        var sourcePath = Path.GetFullPath(config.FilePath);
        targetPath = Path.GetFullPath(targetPath);

        if (PathsAreEqual(sourcePath, targetPath))
            throw new InvalidOperationException("Quelle und Ziel duerfen nicht identisch sein.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Die aktive Datenbankdatei wurde nicht gefunden.", sourcePath);

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Das Backup-Ziel hat kein gueltiges Verzeichnis.");
        Directory.CreateDirectory(targetDirectory);

        var operationId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var backupTemp = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.backup-{operationId}.tmp");
        var displacedBackup = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.previous-{operationId}.bak");

        try
        {
            Database.Instance.Close();
            try
            {
                SqliteConnection.ClearAllPools();
                CopyFileDurably(sourcePath, backupTemp);
                ValidateSqliteBackup(backupTemp);
            }
            finally
            {
                // Revalidate the exact pre-backup session before publishing the
                // copied database at the user-selected destination. Reopen must
                // never adopt a newer stamp after a concurrent revocation.
                Reopen(config, requireSameSession: true);
            }

            if (File.Exists(targetPath))
            {
                // Both files live in the destination directory, so the switch is
                // atomic and the previous backup remains recoverable until it
                // completed successfully.
                File.Replace(backupTemp, targetPath, displacedBackup, true);
                DeleteTemporaryFile(displacedBackup);
            }
            else
            {
                File.Move(backupTemp, targetPath);
            }
        }
        finally
        {
            DeleteTemporaryFile(backupTemp);
        }
    }

    private static void CopyFileDurably(string sourcePath, string destinationPath)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            FileOptions.WriteThrough);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    public static void RestoreBackup(string backupPath)
    {
        lock (OperationGate)
            RestoreBackupCore(backupPath);
    }

    private static void RestoreBackupCore(string backupPath)
    {
        var config = RequireSqliteConfig();
        var targetPath = Path.GetFullPath(config.FilePath);
        backupPath = Path.GetFullPath(backupPath);

        if (PathsAreEqual(backupPath, targetPath))
            throw new InvalidOperationException(LocalizationManager.Get("RestoreSameFile"));

        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup-Datei wurde nicht gefunden.", backupPath);

        if (!File.Exists(targetPath))
            throw new FileNotFoundException(LocalizationManager.Get("RestoreTargetMissing"), targetPath);

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(LocalizationManager.Get("RestoreTargetMissing"));
        Directory.CreateDirectory(targetDirectory);

        var operationId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var restoreTemp = Path.Combine(targetDirectory, $".{Path.GetFileName(targetPath)}.restore-{operationId}.tmp");
        var emergencyBackup = Path.Combine(
            targetDirectory,
            $"{Path.GetFileNameWithoutExtension(targetPath)}-pre-restore-{operationId}.db");

        var databaseClosed = false;
        var targetReplaced = false;
        var databaseReopened = false;

        try
        {
            // Copy first and validate exactly the bytes that will be installed.
            // The temporary file is deliberately placed next to the destination
            // so File.Replace remains an atomic, same-volume operation.
            File.Copy(backupPath, restoreTemp, false);
            ValidateSqliteBackup(restoreTemp);

            // The integrity check can take a long time for a large database.
            // Revalidate the exact live administrator session immediately before
            // the destructive file exchange.
            RequireLiveAdministratorSession("database.restore.persist");

            databaseClosed = true;
            Database.Instance.Close();
            SqliteConnection.ClearAllPools();

            DeleteSqliteSidecars(targetPath);
            File.Replace(restoreTemp, targetPath, emergencyBackup, true);
            targetReplaced = true;

            DeleteSqliteSidecars(targetPath);
            Reopen(config, requireSameSession: false);
            databaseReopened = true;
        }
        catch (Exception restoreException)
        {
            Exception? recoveryException = null;
            if (databaseClosed)
            {
                try
                {
                    Database.Instance.Close();
                    SqliteConnection.ClearAllPools();
                    DeleteSqliteSidecars(targetPath);

                    if (targetReplaced)
                    {
                        if (!File.Exists(emergencyBackup))
                            throw new FileNotFoundException(LocalizationManager.Get("RestoreRecoveryCopyMissing"), emergencyBackup);

                        RestoreEmergencyCopyAtomically(emergencyBackup, targetPath, targetDirectory, operationId);
                    }

                    Reopen(config, requireSameSession: true);
                    databaseReopened = true;
                }
                catch (Exception ex)
                {
                    recoveryException = ex;
                }
            }

            if (recoveryException != null)
            {
                throw new InvalidOperationException(
                    string.Format(
                        LocalizationManager.Get("RestoreRecoveryFailed"),
                        restoreException.Message,
                        recoveryException.Message,
                        emergencyBackup),
                    new AggregateException(restoreException, recoveryException));
            }

            throw;
        }
        finally
        {
            DeleteTemporaryFile(restoreTemp);

            // Every code path after Close must either reopen the restored DB or
            // recover and reopen the original DB. If that was impossible, the
            // catch above surfaces both failures and the recovery-file path.
            if (databaseClosed && !databaseReopened)
                Database.Instance.Close();
        }

        // The restored database may contain an entirely different user universe.
        // End the old process session before returning to any UI/background work.
        Database.Instance.Close();
        App.ClearSessionState();
    }

    private static void ValidateSqliteBackup(string path)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };

            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            using var reader = command.ExecuteReader();

            var receivedResult = false;
            while (reader.Read())
            {
                receivedResult = true;
                var result = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        string.Format(LocalizationManager.Get("RestoreIntegrityFailed"), result));
                }
            }

            if (!receivedResult)
                throw new InvalidDataException(string.Format(LocalizationManager.Get("RestoreIntegrityFailed"), "no result"));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                string.Format(LocalizationManager.Get("RestoreInvalidBackup"), ex.Message),
                ex);
        }
    }

    private static void RestoreEmergencyCopyAtomically(
        string emergencyBackup,
        string targetPath,
        string targetDirectory,
        string operationId)
    {
        var recoveryTemp = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.recovery-{operationId}.tmp");
        var displacedTarget = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.failed-{operationId}.bak");

        try
        {
            File.Copy(emergencyBackup, recoveryTemp, false);
            ValidateSqliteBackup(recoveryTemp);
            File.Replace(recoveryTemp, targetPath, displacedTarget, true);
            DeleteSqliteSidecars(targetPath);
        }
        finally
        {
            DeleteTemporaryFile(recoveryTemp);
            DeleteTemporaryFile(displacedTarget);
        }
    }

    private static ConnectionConfig RequireSqliteConfig()
    {
        var config = App.CurrentConnectionConfig?.Clone();
        if (config?.Backend != DatabaseBackend.SQLite || string.IsNullOrWhiteSpace(config.FilePath))
            throw new InvalidOperationException("Backup/Restore wird aktuell nur fuer lokale SQLite-Datenbanken unterstuetzt.");
        return config;
    }

    private static void Reopen(ConnectionConfig config, bool requireSameSession)
    {
        Database.Instance.Open(config);
        Database.Instance.EnsureSchema();
        App.CurrentConnectionConfig = config.Clone();
        App.DatabasePath = config.FilePath;

        if (!requireSameSession)
            return;

        var state = Database.Instance.GetUserSessionState(App.CurrentUserId);
        if (state is not { IsActive: true } ||
            !string.Equals(state.Username, App.CurrentUsername, StringComparison.Ordinal) ||
            !string.Equals(state.SecurityStamp, App.CurrentSecurityStamp, StringComparison.Ordinal))
            throw new UnauthorizedAccessException(LocalizationManager.Get("SessionInvalidated"));
    }

    private static void RequireLiveAdministratorSession(string action)
    {
        if (!App.TryValidateCurrentSession(out var state) ||
            state == null ||
            !string.Equals(
                state.Permissions.GetValueOrDefault(PageKeys.Admin),
                "full",
                StringComparison.Ordinal))
        {
            AppLogger.Audit("authorization.denied", action, success: false, new { pageKey = PageKeys.Admin });
            throw new UnauthorizedAccessException(LocalizationManager.Get("SessionInvalidated"));
        }
    }

    private static bool PathsAreEqual(string firstPath, string secondPath)
    {
        var firstFullPath = NormalizePathForComparison(firstPath);
        var secondFullPath = NormalizePathForComparison(secondPath);
        return string.Equals(firstFullPath, secondFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForComparison(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!File.Exists(fullPath))
            return fullPath;

        var linkTarget = new FileInfo(fullPath).ResolveLinkTarget(true);
        return Path.TrimEndingDirectorySeparator(linkTarget?.FullName ?? fullPath);
    }

    private static void DeleteSqliteSidecars(string databasePath)
    {
        DeleteIfExists(databasePath + "-wal");
        DeleteIfExists(databasePath + "-shm");
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            DeleteIfExists(path);
        }
        catch
        {
            // A stale uniquely named temp file is safer than masking the
            // restore/recovery result with a cleanup-only failure.
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
