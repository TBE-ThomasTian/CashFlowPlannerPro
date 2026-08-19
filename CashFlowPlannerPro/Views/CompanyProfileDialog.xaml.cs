using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using Microsoft.Win32;

namespace CashFlowPlannerPro.Views;

public partial class CompanyProfileDialog : Window
{
    private readonly CompanyProfile _profile;

    public CompanyProfileDialog()
    {
        InitializeComponent();
        _profile = CompanyProfileService.Load();
        ApplyLocalization();
        LoadValues();
        Owner = Application.Current.MainWindow?.IsVisible == true ? Application.Current.MainWindow : null;
        SaveButton.ToolTip = TooltipService.Get("Btn_Save");
        CancelButton.ToolTip = TooltipService.Get("Btn_Cancel");
        ChangeLogoButton.ToolTip = TooltipService.Get("Btn_ChangeAvatar");
        RemoveLogoButton.ToolTip = TooltipService.Get("Btn_RemoveAvatar");
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("CompanyProfileTitle");
        TitleText.Text = LocalizationManager.Get("CompanyProfileTitle");
        CompanyNameLabel.Text = LocalizationManager.Get("CompanyProfileName");
        Address1Label.Text = LocalizationManager.Get("CompanyProfileAddress1");
        Address2Label.Text = LocalizationManager.Get("CompanyProfileAddress2");
        EmailLabel.Text = LocalizationManager.Get("CompanyProfileEmail");
        PhoneLabel.Text = LocalizationManager.Get("CompanyProfilePhone");
        WebsiteLabel.Text = LocalizationManager.Get("CompanyProfileWebsite");
        TaxIdLabel.Text = LocalizationManager.Get("CompanyProfileTaxId");
        ChangeLogoButton.Content = LocalizationManager.Get("CompanyProfileChangeLogo");
        RemoveLogoButton.Content = LocalizationManager.Get("CompanyProfileRemoveLogo");
        CancelButton.Content = LocalizationManager.Get("Cancel");
        SaveButton.Content = LocalizationManager.Get("Save");
    }

    private void LoadValues()
    {
        TbCompanyName.Text = _profile.CompanyName;
        TbAddress1.Text = _profile.AddressLine1;
        TbAddress2.Text = _profile.AddressLine2;
        TbEmail.Text = _profile.ContactEmail;
        TbPhone.Text = _profile.ContactPhone;
        TbWebsite.Text = _profile.Website;
        TbTaxId.Text = _profile.TaxId;
        LogoImage.Source = AvatarHelper.Base64ToImage(_profile.LogoBase64)
            ?? AvatarHelper.GetDefaultAvatar(string.IsNullOrWhiteSpace(_profile.CompanyName) ? "CF" : _profile.CompanyName);
    }

    private void ChangeLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Bilder|*.jpg;*.jpeg;*.png|Alle Dateien|*.*",
            Title = LocalizationManager.Get("CompanyProfileLogoDialog")
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _profile.LogoBase64 = AvatarHelper.LoadAndValidateImage(dlg.FileName) ?? "";
            LoadValues();
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(ex.Message, LocalizationManager.Get("CompanyProfileTitle"));
        }
    }

    private void RemoveLogo_Click(object sender, RoutedEventArgs e)
    {
        _profile.LogoBase64 = "";
        LoadValues();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _profile.CompanyName = TbCompanyName.Text.Trim();
        _profile.AddressLine1 = TbAddress1.Text.Trim();
        _profile.AddressLine2 = TbAddress2.Text.Trim();
        _profile.ContactEmail = TbEmail.Text.Trim();
        _profile.ContactPhone = TbPhone.Text.Trim();
        _profile.Website = TbWebsite.Text.Trim();
        _profile.TaxId = TbTaxId.Text.Trim();

        CompanyProfileService.Save(_profile);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Chrome_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindParent<TextBoxBase>(source) == null &&
            FindParent<Button>(source) == null &&
            FindParent<ScrollBar>(source) == null)
        {
            try { DragMove(); } catch { }
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match) return match;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    public static bool ShowDialogWindow(Window? owner = null)
    {
        var dlg = new CompanyProfileDialog();
        if (owner != null)
            dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }
}
