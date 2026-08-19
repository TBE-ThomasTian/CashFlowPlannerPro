using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class ApplicationSettingsDialog : Window
{
    private readonly int _originalScalePercent;
    private bool _initializing = true;
    private bool _saved;

    public ApplicationSettingsDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        _originalScalePercent = UiScaleService.CurrentPercent;

        ApplyLocalization();
        UiSizeCombo.SelectedValue = _originalScalePercent;
        LanguageCombo.SelectedValue = LocalizationManager.CurrentLanguageCode;
        _initializing = false;

        SaveButton.ToolTip = TooltipService.Get("Btn_Save");
        CancelButton.ToolTip = TooltipService.Get("Btn_Cancel");
        Closed += (_, _) =>
        {
            if (!_saved)
                UiScaleService.PreviewPercent(_originalScalePercent);
        };
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("SettingsTitle");
        TitleText.Text = LocalizationManager.Get("SettingsTitle");
        AppearanceTitleText.Text = LocalizationManager.Get("SettingsAppearance");
        UiSizeLabel.Text = LocalizationManager.Get("SettingsUiSize");
        UiSizeHintText.Text = LocalizationManager.Get("SettingsUiSizeHint");
        LanguageLabel.Text = LocalizationManager.Get("SettingsLanguage");
        CompanyProfileButton.Content = LocalizationManager.Get("CompanyProfileButton");
        CancelButton.Content = LocalizationManager.Get("Cancel");
        SaveButton.Content = LocalizationManager.Get("Save");

        UiSizeCombo.ItemsSource = new[]
        {
            new ScaleOption(90, LocalizationManager.Get("UiSizeSmall")),
            new ScaleOption(100, LocalizationManager.Get("UiSizeDefault")),
            new ScaleOption(115, LocalizationManager.Get("UiSizeLarge")),
            new ScaleOption(130, LocalizationManager.Get("UiSizeVeryLarge"))
        };
        UiSizeCombo.DisplayMemberPath = nameof(ScaleOption.Label);
        UiSizeCombo.SelectedValuePath = nameof(ScaleOption.Percent);

        LanguageCombo.ItemsSource = new[]
        {
            new LanguageOption("de", LocalizationManager.Get("LanguageGerman")),
            new LanguageOption("en", LocalizationManager.Get("LanguageEnglish"))
        };
        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.Label);
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Code);
    }

    private void UiSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || UiSizeCombo.SelectedValue is not int percent)
            return;

        // Keep this dialog stable while its ComboBox is completing the selection.
        // The main window and all other open windows still update immediately.
        UiScaleService.PreviewPercent(percent, this);
    }

    private void CompanyProfile_Click(object sender, RoutedEventArgs e)
    {
        CompanyProfileDialog.ShowDialogWindow(this);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (UiSizeCombo.SelectedValue is not int percent ||
            LanguageCombo.SelectedValue is not string languageCode)
            return;

        try
        {
            UiScaleService.SavePercent(percent);
            LocalizationManager.SetLanguage(languageCode);
            _saved = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("SettingsSaveError"), ex.Message),
                LocalizationManager.Get("SettingsTitle"));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }

    private void Chrome_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindParent<Button>(source) == null &&
            FindParent<ComboBox>(source) == null &&
            FindParent<ComboBoxItem>(source) == null &&
            FindParent<ScrollBar>(source) == null)
        {
            try { DragMove(); } catch { }
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
                return match;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    public static bool ShowDialogWindow() => new ApplicationSettingsDialog().ShowDialog() == true;

    private sealed record ScaleOption(int Percent, string Label);
    private sealed record LanguageOption(string Code, string Label);
}
