using System.Diagnostics;
using System.Security.Cryptography;
using CashFlowPlannerPro.Updater;

if (args.Length == 2 && string.Equals(args[0], "--verify-package", StringComparison.Ordinal))
{
    try
    {
        UpdatePackageSecurity.VerifyExtractedPackage(
            Path.GetFullPath(args[1]),
            Environment.ProcessPath ?? throw new InvalidOperationException("Updater process path is unavailable."));
        return 0;
    }
    catch
    {
        return 1;
    }
}

if (args.Length != 6)
    return 1;

string? extractDirectory = null;
string? rollbackDirectory = null;

try
{
    var zipPath = Path.GetFullPath(args[0]);
    var appDirectory = NormalizeDirectory(args[1]);
    var appExePath = Path.GetFullPath(args[2]);
    var expectedSha256 = args[4].Trim();
    var expectedVersion = args[5].Trim();
    if (!int.TryParse(args[3], out var appProcessId) ||
        !IsSha256(expectedSha256) ||
        !Version.TryParse(expectedVersion, out var requestedVersion) ||
        !ValidateInstallationPaths(appDirectory, appExePath))
    {
        return 1;
    }

    extractDirectory = Path.Combine(
        Path.GetTempPath(),
        "CashFlowPlannerPro_Update_" + Guid.NewGuid().ToString("N"));
    rollbackDirectory = Path.Combine(
        Path.GetTempPath(),
        "CashFlowPlannerPro_Rollback_" + Guid.NewGuid().ToString("N"));

    var installedVersion = UpdatePackageSecurity.GetValidInstalledApplicationVersion(
        appExePath,
        Environment.ProcessPath!);
    if (requestedVersion <= installedVersion ||
        !File.Exists(zipPath) ||
        !VerifySha256(zipPath, expectedSha256))
        return 1;

    WaitForVerifiedProcessExit(appProcessId, appExePath);
    Directory.CreateDirectory(extractDirectory);
    UpdatePackageSecurity.ExtractArchiveSafely(zipPath, extractDirectory);
    var verifiedPackage = UpdatePackageSecurity.VerifyExtractedPackage(extractDirectory, Environment.ProcessPath!);
    if (!string.Equals(verifiedPackage.Version, expectedVersion, StringComparison.Ordinal) ||
        !Version.TryParse(verifiedPackage.Version, out var packageVersion) ||
        packageVersion <= installedVersion)
    {
        throw new InvalidDataException("The signed package version is not the requested upgrade.");
    }

    ApplyUpdateWithRollback(extractDirectory, appDirectory, rollbackDirectory, verifiedPackage);

    Process.Start(new ProcessStartInfo
    {
        FileName = appExePath,
        WorkingDirectory = appDirectory,
        UseShellExecute = true
    });

    TryDeleteDirectory(extractDirectory);
    TryDeleteDirectory(rollbackDirectory);
    TryDeleteFile(zipPath);
    return 0;
}
catch
{
    if (extractDirectory != null)
        TryDeleteDirectory(extractDirectory);
    if (rollbackDirectory != null)
        TryDeleteDirectory(rollbackDirectory);
    return 1;
}

static bool ValidateInstallationPaths(string appDirectory, string appExePath)
{
    if (!Directory.Exists(appDirectory) ||
        !File.Exists(appExePath) ||
        !string.Equals(Path.GetFileName(appExePath), "CashFlowPlannerPro.exe", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(NormalizeDirectory(Path.GetDirectoryName(appExePath)!), appDirectory, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var updaterPath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(updaterPath) ||
        !string.Equals(
            Path.GetFileName(updaterPath),
            "CashFlowPlannerPro.Updater.exe",
            StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var expectedUpdaterDirectory = NormalizeDirectory(Path.Combine(appDirectory, "updater"));
    var actualUpdaterDirectory = NormalizeDirectory(Path.GetDirectoryName(Path.GetFullPath(updaterPath))!);
    return string.Equals(actualUpdaterDirectory, expectedUpdaterDirectory, StringComparison.OrdinalIgnoreCase);
}

static void WaitForVerifiedProcessExit(int processId, string expectedPath)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        var actualPath = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(actualPath) ||
            !string.Equals(Path.GetFullPath(actualPath), expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The target process does not belong to this installation.");
        }

        if (!process.WaitForExit(30_000))
            throw new TimeoutException("The application did not exit before the update timeout.");
    }
    catch (ArgumentException)
    {
        // The expected process already exited between launching the updater and this check.
    }
}

static bool IsSha256(string value) =>
    value.Length == 64 && value.All(Uri.IsHexDigit);

static bool VerifySha256(string path, string expected)
{
    using var stream = File.OpenRead(path);
    var actual = SHA256.HashData(stream);
    return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected));
}

static void ApplyUpdateWithRollback(
    string sourceDirectory,
    string targetDirectory,
    string rollbackDirectory,
    VerifiedPackage verifiedPackage)
{
    Directory.CreateDirectory(rollbackDirectory);
    var createdFiles = new List<string>();
    var backedUpFiles = new List<(string Backup, string Destination)>();

    try
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            if (IsUpdaterPath(relativePath) || IsPackageMetadataPath(relativePath))
                continue;

            var manifestPath = relativePath.Replace('\\', '/');
            if (!verifiedPackage.Files.TryGetValue(manifestPath, out var expectedFile))
                throw new InvalidDataException($"An update file is not present in the signed manifest: {manifestPath}");

            var destination = ResolveContainedPath(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                var backup = ResolveContainedPath(rollbackDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, overwrite: false);
                backedUpFiles.Add((backup, destination));
            }
            else
            {
                createdFiles.Add(destination);
            }

            ReplaceFileAtomically(sourceFile, destination, expectedFile);
        }
    }
    catch
    {
        foreach (var createdFile in createdFiles.AsEnumerable().Reverse())
            TryDeleteFile(createdFile);
        foreach (var (backup, destination) in backedUpFiles.AsEnumerable().Reverse())
            RestoreFileAtomically(backup, destination);
        throw;
    }
}

static void ReplaceFileAtomically(
    string source,
    string destination,
    VerifiedPackageFile? expectedFile = null)
{
    var directory = Path.GetDirectoryName(destination)!;
    var temp = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.update.tmp");
    var displaced = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.old.tmp");
    try
    {
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
        {
            if (expectedFile == null)
            {
                input.CopyTo(output);
            }
            else
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                long copiedBytes = 0;
                while (true)
                {
                    var read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    copiedBytes = checked(copiedBytes + read);
                    if (copiedBytes > expectedFile.Length)
                        throw new InvalidDataException("An update file changed after package verification.");
                    hash.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }

                var actualHash = hash.GetHashAndReset();
                if (copiedBytes != expectedFile.Length ||
                    !CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(expectedFile.Sha256)))
                {
                    throw new CryptographicException("An update file changed after package verification.");
                }
            }
            output.Flush(flushToDisk: true);
        }

        if (File.Exists(destination))
        {
            File.Replace(temp, destination, displaced, ignoreMetadataErrors: true);
            TryDeleteFile(displaced);
        }
        else
        {
            File.Move(temp, destination);
        }
    }
    finally
    {
        TryDeleteFile(temp);
    }
}

static void RestoreFileAtomically(string backup, string destination)
{
    if (!File.Exists(backup))
        throw new FileNotFoundException("An update rollback file is missing.", backup);
    ReplaceFileAtomically(backup, destination);
}

static bool IsUpdaterPath(string relativePath) =>
    relativePath.Equals("updater", StringComparison.OrdinalIgnoreCase) ||
    relativePath.StartsWith("updater" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
    relativePath.StartsWith("updater" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

static bool IsPackageMetadataPath(string relativePath) =>
    string.Equals(relativePath, UpdatePackageSecurity.ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(relativePath, UpdatePackageSecurity.ManifestSignatureFileName, StringComparison.OrdinalIgnoreCase);

static string ResolveContainedPath(string root, string relativePath)
{
    var rootPrefix = NormalizeDirectory(root) + Path.DirectorySeparatorChar;
    var result = Path.GetFullPath(Path.Combine(root, relativePath));
    if (!result.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("A staged update path escapes its target directory.");
    return result;
}

static string NormalizeDirectory(string path) =>
    Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

static void TryDeleteDirectory(string path)
{
    try
    {
        if (!Directory.Exists(path))
            return;

        var fullPath = NormalizeDirectory(path);
        var tempRoot = NormalizeDirectory(Path.GetTempPath());
        var parent = NormalizeDirectory(Path.GetDirectoryName(fullPath)!);
        var name = Path.GetFileName(fullPath);
        if (!string.Equals(parent, tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !(name.StartsWith("CashFlowPlannerPro_Update_", StringComparison.Ordinal) ||
              name.StartsWith("CashFlowPlannerPro_Rollback_", StringComparison.Ordinal)))
        {
            return;
        }

        var rootInfo = new DirectoryInfo(fullPath);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories)
                .Select(entry => File.GetAttributes(entry))
                .Any(attributes => (attributes & FileAttributes.ReparsePoint) != 0))
        {
            return;
        }

        Directory.Delete(fullPath, recursive: true);
    }
    catch { }
}

static void TryDeleteFile(string path)
{
    try
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
    }
    catch { }
}
