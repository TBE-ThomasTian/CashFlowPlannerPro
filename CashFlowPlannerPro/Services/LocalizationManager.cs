using System.Globalization;
using System.IO;
using System.Resources;

namespace CashFlowPlannerPro.Services;

public static class LocalizationManager
{
    private static readonly ResourceManager ResourceManager = new(
        "CashFlowPlannerPro.Resources.Strings",
        typeof(LocalizationManager).Assembly);

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");

    private static readonly string LanguageFile = Path.Combine(SettingsDir, "ui-language.txt");

    public static event EventHandler? LanguageChanged;

    public static string CurrentLanguageCode { get; private set; } = "de";

    public static void LoadSavedLanguage()
    {
        try
        {
            if (File.Exists(LanguageFile))
            {
                var code = File.ReadAllText(LanguageFile).Trim();
                if (!string.IsNullOrWhiteSpace(code))
                    SetLanguage(code, false);
            }
        }
        catch
        {
            SetLanguage("de", false);
        }
    }

    public static void SetLanguage(string languageCode, bool persist = true)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? "de" : languageCode.Trim().ToLowerInvariant();
        var culture = new CultureInfo(normalized);

        CurrentLanguageCode = culture.TwoLetterISOLanguageName;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (persist)
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(LanguageFile, CurrentLanguageCode);
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key)
        => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
