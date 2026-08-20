using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CashFlowPlannerPro.Updater;

internal static partial class UpdatePackageSecurity
{
    internal const string ManifestFileName = "update-manifest.json";
    internal const string ManifestSignatureFileName = "update-manifest.p7s";
    internal const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    internal const int MaxArchiveEntries = 20_000;
    private const int MaxManifestBytes = 8 * 1024 * 1024;
    private const int MaxSignatureBytes = 2 * 1024 * 1024;
    private const string AuthenticodePathEnvironmentVariable = "CFPP_AUTHENTICODE_PATH";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    internal static void ExtractArchiveSafely(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("The update package contains too many entries.");

        var destinationPrefix = EnsureTrailingSeparator(Path.GetFullPath(destination));
        long expandedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (IsSymbolicLink(entry))
                throw new InvalidDataException("Symbolic links are not allowed in update packages.");

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaxExpandedBytes)
                throw new InvalidDataException("The update package is too large when expanded.");

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update package contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    internal static VerifiedPackage VerifyExtractedPackage(string packageRoot, string updaterPath)
    {
        var root = Path.GetFullPath(packageRoot);
        var manifestPath = Path.Combine(root, ManifestFileName);
        var signaturePath = Path.Combine(root, ManifestSignatureFileName);
        var manifestBytes = ReadBoundedFile(manifestPath, MaxManifestBytes);
        var signatureBytes = ReadBoundedFile(signaturePath, MaxSignatureBytes);

        using var updaterSigner = LoadValidAuthenticodeSigner(updaterPath);
        using var manifestSigner = VerifyDetachedManifestSignature(manifestBytes, signatureBytes);
        if (!CertificateHashesEqual(updaterSigner, manifestSigner))
            throw new CryptographicException("The package manifest was not signed by the updater publisher.");

        var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 16
        }) ?? throw new InvalidDataException("The package manifest is empty.");

        var verifiedPackage = ValidateManifestAndPayload(root, manifest);

        var stagedApp = Path.Combine(root, "CashFlowPlannerPro.exe");
        using var stagedAppSigner = LoadValidAuthenticodeSigner(stagedApp);
        if (!CertificateHashesEqual(updaterSigner, stagedAppSigner))
            throw new CryptographicException("The staged application has a different publisher signature.");
        if (!Version.TryParse(manifest.Version, out var manifestVersion) ||
            !VersionsMatchRelease(manifestVersion, GetFileVersion(stagedApp)))
        {
            throw new InvalidDataException("The signed package version does not match the staged application.");
        }

        return verifiedPackage;
    }

    internal static Version GetValidInstalledApplicationVersion(string appPath, string updaterPath)
    {
        using var updaterSigner = LoadValidAuthenticodeSigner(updaterPath);
        using var appSigner = LoadValidAuthenticodeSigner(appPath);
        if (!CertificateHashesEqual(updaterSigner, appSigner))
            throw new CryptographicException("The installed application and updater have different publishers.");
        return GetFileVersion(appPath);
    }

    private static X509Certificate2 VerifyDetachedManifestSignature(byte[] manifest, byte[] signature)
    {
        var cms = new SignedCms(new ContentInfo(manifest), detached: true);
        try
        {
            cms.Decode(signature);
            if (cms.SignerInfos.Count != 1)
                throw new CryptographicException("The package manifest must have exactly one signer.");

            var signerInfo = cms.SignerInfos[0];
            if (!string.Equals(signerInfo.DigestAlgorithm.Value, Sha256Oid, StringComparison.Ordinal))
                throw new CryptographicException("The package manifest must use SHA-256.");

            // false validates both the CMS signature and the certificate chain.
            cms.CheckSignature(verifySignatureOnly: false);
            var certificate = signerInfo.Certificate
                ?? throw new CryptographicException("The package manifest signer certificate is missing.");
            return new X509Certificate2(certificate);
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CryptographicException("The package manifest signature is invalid.", ex);
        }
    }

    private static VerifiedPackage ValidateManifestAndPayload(string root, PackageManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || !StableVersionRegex().IsMatch(manifest.Version))
            throw new InvalidDataException("The package manifest version is invalid.");
        if (manifest.Files is not { Count: > 0 } || manifest.Files.Count > MaxArchiveEntries)
            throw new InvalidDataException("The package manifest file list is invalid.");

        var expected = new Dictionary<string, VerifiedPackageFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files)
        {
            var path = NormalizeManifestPath(item.Path);
            if (IsMetadataPath(path) || item.Length < 0 || item.Length > MaxExpandedBytes || !IsSha256(item.Sha256))
                throw new InvalidDataException($"The package manifest contains an invalid entry: {path}");
            if (!expected.TryAdd(path, new VerifiedPackageFile(item.Length, item.Sha256.ToUpperInvariant())))
                throw new InvalidDataException($"The package manifest contains a duplicate path: {path}");
        }

        if (!expected.ContainsKey("CashFlowPlannerPro.exe") ||
            !expected.ContainsKey("updater/CashFlowPlannerPro.Updater.exe"))
        {
            throw new InvalidDataException("The package manifest is missing required executables.");
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            if ((new DirectoryInfo(directoryPath).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not allowed in update packages.");
        }

        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var file = new FileInfo(filePath);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not allowed in update packages.");

            var relativePath = NormalizeManifestPath(Path.GetRelativePath(root, file.FullName));
            if (IsMetadataPath(relativePath))
                continue;
            if (!actual.TryAdd(relativePath, file.FullName))
                throw new InvalidDataException($"The package contains a duplicate path: {relativePath}");
        }

        if (actual.Count != expected.Count ||
            actual.Keys.Except(expected.Keys, StringComparer.OrdinalIgnoreCase).Any() ||
            expected.Keys.Except(actual.Keys, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new InvalidDataException("The package contents do not exactly match the signed manifest.");
        }

        foreach (var (relativePath, item) in expected)
        {
            var actualPath = actual[relativePath];
            var fileInfo = new FileInfo(actualPath);
            if (fileInfo.Length != item.Length)
                throw new InvalidDataException($"The package file length is invalid: {relativePath}");

            using var stream = File.OpenRead(actualPath);
            var actualHash = SHA256.HashData(stream);
            var expectedHash = Convert.FromHexString(item.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                throw new CryptographicException($"The package file hash is invalid: {relativePath}");
        }

        return new VerifiedPackage(manifest.Version, expected);
    }

    internal static bool VerifyAuthenticodeSignature(string path)
    {
        if (!File.Exists(path))
            return false;

        var powerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powerShell))
            return false;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powerShell,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.Environment[AuthenticodePathEnvironmentVariable] = Path.GetFullPath(path);
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(
            "$ErrorActionPreference = 'Stop'; " +
            $"$signature = Get-AuthenticodeSignature -LiteralPath $env:{AuthenticodePathEnvironmentVariable}; " +
            "if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) { exit 1 }");

        if (!process.Start())
            return false;
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return false;
        }

        return process.ExitCode == 0;
    }

    private static X509Certificate2 LoadValidAuthenticodeSigner(string path)
    {
        if (!VerifyAuthenticodeSignature(path))
            throw new CryptographicException("The Authenticode signature is not valid.");

        try
        {
#pragma warning disable SYSLIB0057 // No non-obsolete managed API reads a PE Authenticode signer certificate.
            using var certificate = X509Certificate.CreateFromSignedFile(path);
            return new X509Certificate2(certificate);
#pragma warning restore SYSLIB0057
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            throw new CryptographicException("The Authenticode signer certificate could not be read.", ex);
        }
    }

    private static byte[] ReadBoundedFile(string path, int maxBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maxBytes)
            throw new InvalidDataException($"A required package metadata file is missing or too large: {info.Name}");
        return File.ReadAllBytes(info.FullName);
    }

    private static bool CertificateHashesEqual(X509Certificate2 first, X509Certificate2 second) =>
        CryptographicOperations.FixedTimeEquals(
            first.GetCertHash(HashAlgorithmName.SHA256),
            second.GetCertHash(HashAlgorithmName.SHA256));

    private static Version GetFileVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.FileMajorPart < 0 || info.FileMinorPart < 0 || info.FileBuildPart < 0 || info.FilePrivatePart < 0)
            throw new InvalidDataException("The signed application file version is invalid.");
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    private static bool VersionsMatchRelease(Version releaseVersion, Version fileVersion) =>
        releaseVersion.Major == fileVersion.Major &&
        releaseVersion.Minor == fileVersion.Minor &&
        releaseVersion.Build == fileVersion.Build;

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        return unixMode == UnixSymbolicLink;
    }

    private static string NormalizeManifestPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
            throw new InvalidDataException("The package manifest contains an empty path.");

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            normalized.Contains(":", StringComparison.Ordinal) ||
            normalized.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The package manifest contains an unsafe path: {path}");
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"The package manifest contains an unsafe path: {path}");
        return string.Join('/', segments);
    }

    private static bool IsMetadataPath(string path) =>
        string.Equals(path, ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, ManifestSignatureFileName, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSeparator(string path) =>
        Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex StableVersionRegex();
}

internal sealed class PackageManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public List<PackageManifestFile> Files { get; init; } = [];
}

internal sealed class PackageManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("length")]
    public long Length { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;
}

internal sealed record VerifiedPackage(
    string Version,
    IReadOnlyDictionary<string, VerifiedPackageFile> Files);

internal sealed record VerifiedPackageFile(long Length, string Sha256);
