using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Views;

namespace CashFlowPlannerPro.Services;

public static class UpdateService
{
    private const string UpdateInfoUrl = "https://www.building-engineering.de/Download/version.json";

    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var json = await client.GetStringAsync(UpdateInfoUrl);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (info == null || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.Url))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("UpdateInvalidResponse"),
                    LocalizationManager.Get("UpdateTitle"));
                return;
            }

            var currentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            if (!Version.TryParse(info.Version, out var latestVersion))
            {
                ModernMessageBox.ShowError(
                    LocalizationManager.Get("UpdateInvalidVersion"),
                    LocalizationManager.Get("UpdateTitle"));
                return;
            }

            if (latestVersion <= currentVersion)
            {
                ModernMessageBox.Show(
                    string.Format(LocalizationManager.Get("UpdateUpToDate"), currentVersion),
                    LocalizationManager.Get("UpdateTitle"));
                return;
            }

            var notes = string.IsNullOrWhiteSpace(info.Notes)
                ? LocalizationManager.Get("UpdateNoNotes")
                : info.Notes.Trim();

            var message = string.Format(
                LocalizationManager.Get("UpdateAvailableMessage"),
                currentVersion,
                latestVersion,
                notes);

            if (ModernMessageBox.ShowConfirm(message, LocalizationManager.Get("UpdateAvailableTitle")))
                OpenDownloadUrl(info.Url);
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("UpdateCheckFailed"), ex.Message),
                LocalizationManager.Get("UpdateTitle"));
        }
    }

    private static void OpenDownloadUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
