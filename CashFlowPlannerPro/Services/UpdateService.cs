using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Views;

namespace CashFlowPlannerPro.Services;

public static partial class UpdateService
{
    private const string UpdateInfoUrl = "https://www.building-engineering.de/Download/version.json";
    private const int MaxManifestBytes = 256 * 1024;
    private const long MaxPackageBytes = 1024L * 1024 * 1024;
    private const int MaxRedirects = 5;

    private static readonly HashSet<string> PackageOriginHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "building-engineering.de",
        "www.building-engineering.de",
        "github.com"
    };

    private static readonly HashSet<string> PackageRedirectHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "building-engineering.de",
        "www.building-engineering.de",
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com"
    };

    /// <summary>
    /// Returns true only after a fully downloaded transport-hash-verified ZIP
    /// was handed to the installed updater. The updater independently requires
    /// the publisher-signed internal file manifest before changing any files.
    /// </summary>
    public static async Task<bool> CheckForUpdatesAsync()
    {
        string? downloadedPackage = null;
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CashFlowPlannerPro-Updater/2");

            using var manifestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await SendWithValidatedRedirectsAsync(
                client,
                new Uri(UpdateInfoUrl),
                IsAllowedManifestUri,
                manifestTimeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxManifestBytes)
                throw new InvalidDataException("Die Update-Antwort ist unerwartet groß.");

            var json = await ReadLimitedStringAsync(response.Content, MaxManifestBytes, manifestTimeout.Token);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
                MaxDepth = 16
            });

            var manifestVersion = info?.Version?.Trim();
            if (info == null ||
                manifestVersion == null ||
                !StableVersionRegex().IsMatch(manifestVersion) ||
                !TryValidateDownloadUrl(info.Url, out var downloadUri) ||
                !IsSha256(info.Sha256))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("UpdateInvalidResponse"),
                    LocalizationManager.Get("UpdateTitle"));
                return false;
            }

            var currentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            if (!Version.TryParse(manifestVersion, out var latestVersion))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("UpdateInvalidVersion"),
                    LocalizationManager.Get("UpdateTitle"));
                return false;
            }

            if (latestVersion <= currentVersion)
            {
                ModernMessageBox.Show(
                    string.Format(LocalizationManager.Get("UpdateUpToDate"), currentVersion),
                    LocalizationManager.Get("UpdateTitle"));
                return false;
            }

            var notes = string.IsNullOrWhiteSpace(info.Notes)
                ? LocalizationManager.Get("UpdateNoNotes")
                : info.Notes.Trim();
            var message = string.Format(
                LocalizationManager.Get("UpdateAvailableMessage"),
                currentVersion,
                latestVersion,
                notes);
            if (!ModernMessageBox.ShowConfirm(message, LocalizationManager.Get("UpdateAvailableTitle")))
                return false;

            downloadedPackage = await DownloadPackageAsync(client, downloadUri, info.Sha256);
            StartUpdater(downloadedPackage, info.Sha256, manifestVersion);
            AppLogger.Audit("update.launch", manifestVersion, success: true, new { host = downloadUri.IdnHost });
            return true;
        }
        catch (Exception ex)
        {
            if (downloadedPackage != null)
                TryDeleteDownloadedPackage(downloadedPackage);
            var reference = AppLogger.LogException("update.check_or_download_failed", ex);
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("UpdateCheckFailed"), $"Referenz: {reference}"),
                LocalizationManager.Get("UpdateTitle"));
            return false;
        }
    }

    private static async Task<string> DownloadPackageAsync(HttpClient client, Uri uri, string expectedSha256)
    {
        using var response = await SendWithValidatedRedirectsAsync(
            client,
            uri,
            IsAllowedPackageResponseUri,
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxPackageBytes)
            throw new InvalidDataException("Das Update-Paket ist unerwartet groß.");

        var downloadDirectory = Path.Combine(
            Path.GetTempPath(),
            "CashFlowPlannerPro_Download_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloadDirectory);
        var destination = Path.Combine(downloadDirectory, "CashFlowPlannerPro-update.zip");

        try
        {
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long totalBytes = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer);
                if (read == 0)
                    break;
                totalBytes = checked(totalBytes + read);
                if (totalBytes > MaxPackageBytes)
                    throw new InvalidDataException("Das Update-Paket ist unerwartet groß.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read));
            }

            await output.FlushAsync();
            output.Flush(flushToDisk: true);
            var actualHash = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(
                    actualHash,
                    Convert.FromHexString(expectedSha256.Trim())))
            {
                throw new CryptographicException("Die SHA-256-Prüfsumme des Update-Pakets stimmt nicht.");
            }

            return destination;
        }
        catch
        {
            TryDeleteDownloadedPackage(destination);
            throw;
        }
    }

    private static void StartUpdater(string zipPath, string expectedSha256, string expectedVersion)
    {
        var appExePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(appExePath) ||
            !string.Equals(Path.GetFileName(appExePath), "CashFlowPlannerPro.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Das Update kann nur aus der installierten Anwendung gestartet werden.");
        }

        var appDirectory = Path.GetDirectoryName(Path.GetFullPath(appExePath))
            ?? throw new InvalidOperationException("Das Installationsverzeichnis wurde nicht gefunden.");
        var updaterPath = Path.Combine(appDirectory, "updater", "CashFlowPlannerPro.Updater.exe");
        if (!File.Exists(updaterPath))
            throw new FileNotFoundException("Der sichere Updater wurde nicht gefunden.", updaterPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            WorkingDirectory = Path.GetDirectoryName(updaterPath)!,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.GetFullPath(zipPath));
        startInfo.ArgumentList.Add(appDirectory);
        startInfo.ArgumentList.Add(Path.GetFullPath(appExePath));
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(expectedSha256.Trim().ToUpperInvariant());
        startInfo.ArgumentList.Add(expectedVersion);

        if (Process.Start(startInfo) == null)
            throw new InvalidOperationException("Der sichere Updater konnte nicht gestartet werden.");
    }

    private static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(
        HttpClient client,
        Uri initialUri,
        Func<Uri, bool> validator,
        CancellationToken cancellationToken)
    {
        var current = initialUri;
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            if (!validator(current))
                throw new InvalidDataException("Die Update-Adresse ist nicht zugelassen.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location == null)
                throw new InvalidDataException("Die Update-Weiterleitung enthält kein Ziel.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }

        throw new HttpRequestException("Das Update enthält zu viele Weiterleitungen.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static async Task<string> ReadLimitedStringAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("Die Update-Antwort ist unerwartet groß.");
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool TryValidateDownloadUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            !IsSafeHttpsUri(parsed) ||
            !PackageOriginHosts.Contains(parsed.IdnHost) ||
            !parsed.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parsed.IdnHost.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            !parsed.AbsolutePath.StartsWith(
                "/TBE-ThomasTian/CashFlowPlannerPro/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsAllowedManifestUri(Uri uri) =>
        IsSafeHttpsUri(uri) &&
        (uri.IdnHost.Equals("building-engineering.de", StringComparison.OrdinalIgnoreCase) ||
         uri.IdnHost.Equals("www.building-engineering.de", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.Equals("/Download/version.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedPackageResponseUri(Uri uri) =>
        IsSafeHttpsUri(uri) && PackageRedirectHosts.Contains(uri.IdnHost);

    private static bool IsSafeHttpsUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        (uri.IsDefaultPort || uri.Port == 443);

    private static bool IsSha256(string? value) =>
        value?.Trim() is { Length: 64 } hash && hash.All(Uri.IsHexDigit);

    private static void TryDeleteDownloadedPackage(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                File.Delete(fullPath);

            var directory = Path.GetDirectoryName(fullPath);
            if (directory != null &&
                Path.GetFileName(directory).StartsWith("CashFlowPlannerPro_Download_", StringComparison.Ordinal) &&
                Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
        catch
        {
            // A stale uniquely named download is safer than broad cleanup.
        }
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex StableVersionRegex();
}
