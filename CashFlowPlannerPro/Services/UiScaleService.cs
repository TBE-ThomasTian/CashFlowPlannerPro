using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CashFlowPlannerPro.Services;

/// <summary>
/// Applies a workstation-wide UI scale to every application window.
/// This deliberately scales the complete layout because many views use
/// explicit font sizes, paddings, and row heights that must remain in sync.
/// </summary>
public static class UiScaleService
{
    public const int DefaultPercent = 100;
    public static IReadOnlyList<int> AllowedPercents { get; } = [90, 100, 115, 130];

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro");

    private static readonly string ScaleFile = Path.Combine(SettingsDirectory, "ui-scale.txt");
    private static bool _initialized;

    public static readonly DependencyProperty IsScalingEnabledProperty = DependencyProperty.RegisterAttached(
        "IsScalingEnabled",
        typeof(bool),
        typeof(UiScaleService),
        new FrameworkPropertyMetadata(true));

    public static int CurrentPercent { get; private set; } = DefaultPercent;

    public static void SetIsScalingEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsScalingEnabledProperty, value);

    public static bool GetIsScalingEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsScalingEnabledProperty);

    public static void Initialize()
    {
        if (_initialized)
            return;

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Window_Loaded),
            handledEventsToo: true);

        _initialized = true;
        LoadSavedScale();
    }

    public static void LoadSavedScale()
    {
        var percent = DefaultPercent;
        try
        {
            if (File.Exists(ScaleFile) &&
                int.TryParse(File.ReadAllText(ScaleFile).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var storedPercent) &&
                IsAllowed(storedPercent))
            {
                percent = storedPercent;
            }
        }
        catch
        {
            // A damaged or inaccessible local preference must never prevent login.
            percent = DefaultPercent;
        }

        ApplyPercent(percent);
    }

    public static void PreviewPercent(int percent, Window? excludedWindow = null) =>
        ApplyPercent(percent, excludedWindow);

    public static void SavePercent(int percent)
    {
        EnsureAllowed(percent);
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(ScaleFile, percent.ToString(CultureInfo.InvariantCulture));
        ApplyPercent(percent);
    }

    private static void ApplyPercent(int percent, Window? excludedWindow = null)
    {
        EnsureAllowed(percent);
        CurrentPercent = percent;

        if (Application.Current == null)
            return;

        foreach (Window window in Application.Current.Windows)
        {
            if (ReferenceEquals(window, excludedWindow))
                continue;
            ApplyToWindow(window);
        }
    }

    private static void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            ApplyToWindow(window);
    }

    private static void ApplyToWindow(Window window)
    {
        if (window.Content is not FrameworkElement root)
            return;

        if (!GetIsScalingEnabled(window))
        {
            root.LayoutTransform = Transform.Identity;
            return;
        }

        var factor = CurrentPercent / 100d;
        root.LayoutTransform = CurrentPercent == DefaultPercent
            ? Transform.Identity
            : new ScaleTransform(factor, factor);
        root.UseLayoutRounding = true;
        root.SnapsToDevicePixels = true;
        window.InvalidateMeasure();
    }

    private static bool IsAllowed(int percent) => AllowedPercents.Contains(percent);

    private static void EnsureAllowed(int percent)
    {
        if (!IsAllowed(percent))
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "Unsupported UI scale.");
    }
}
