using System.Linq;
using System.Windows;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class SevDeskImportDialog : Window
{
    private readonly SevDeskImportPreview _preview;

    public SevDeskImportDialog(SevDeskImportPreview preview)
    {
        InitializeComponent();
        _preview = preview;
        CustomersGrid.ItemsSource = _preview.Contacts;
        InvoicesGrid.ItemsSource = _preview.Invoices;
        OffersGrid.ItemsSource = _preview.Offers;
        ApplyLocalization();
        RefreshSelectAllTexts();
    }

    private void ApplyLocalization()
    {
        Title = LocalizationManager.Get("IntegrationsImportDialogTitle");
        TitleText.Text = LocalizationManager.Get("IntegrationsImportDialogTitle");
        SubtitleText.Text = LocalizationManager.Get("IntegrationsImportDialogSubtitle");
        InvoicesTab.Header = LocalizationManager.Get("IntegrationsInvoices");
        OffersTab.Header = LocalizationManager.Get("IntegrationsOffers");
        CustomerNameColumn.Header = LocalizationManager.Get("IntegrationsCustomers");
        CustomerStateColumn.Header = LocalizationManager.Get("IntegrationsState");
        InvoiceNumberColumn.Header = LocalizationManager.Get("IntegrationsInvoiceNumber");
        InvoiceSourceStatusColumn.Header = LocalizationManager.Get("IntegrationsSourceStatus");
        InvoiceCustomerColumn.Header = LocalizationManager.Get("IntegrationsCustomers");
        InvoiceDateColumn.Header = LocalizationManager.Get("IntegrationsDate");
        InvoiceDueDateColumn.Header = LocalizationManager.Get("IntegrationsDueDate");
        InvoiceAmountColumn.Header = LocalizationManager.Get("IntegrationsAmount");
        InvoiceStateColumn.Header = LocalizationManager.Get("IntegrationsImportState");
        OfferNumberColumn.Header = LocalizationManager.Get("IntegrationsOfferNumber");
        OfferSourceStatusColumn.Header = LocalizationManager.Get("IntegrationsSourceStatus");
        OfferCustomerColumn.Header = LocalizationManager.Get("IntegrationsCustomers");
        OfferDateColumn.Header = LocalizationManager.Get("IntegrationsDate");
        OfferExpectedDateColumn.Header = LocalizationManager.Get("IntegrationsExpectedDate");
        OfferAmountColumn.Header = LocalizationManager.Get("IntegrationsAmount");
        OfferProbabilityColumn.Header = LocalizationManager.Get("IntegrationsProbability");
        OfferStateColumn.Header = LocalizationManager.Get("IntegrationsImportState");
        CancelButton.Content = LocalizationManager.Get("Cancel");
        ImportButton.Content = LocalizationManager.Get("IntegrationsImportAction");
    }

    private void SelectAllCustomersCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool isChecked = SelectAllCustomersCheckBox.IsChecked == true;
        foreach (var item in _preview.Contacts)
            item.IsSelected = isChecked;
        CustomersGrid.Items.Refresh();
    }

    private void SelectAllInvoicesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool isChecked = SelectAllInvoicesCheckBox.IsChecked == true;
        foreach (var item in _preview.Invoices)
            item.IsSelected = isChecked;
        InvoicesGrid.Items.Refresh();
    }

    private void SelectAllOffersCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool isChecked = SelectAllOffersCheckBox.IsChecked == true;
        foreach (var item in _preview.Offers)
            item.IsSelected = isChecked;
        OffersGrid.Items.Refresh();
    }

    private void GridSelectionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        RefreshSelectAllTexts();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_preview.Contacts.Any(x => x.IsSelected) && !_preview.Invoices.Any(x => x.IsSelected) && !_preview.Offers.Any(x => x.IsSelected))
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("IntegrationsNothingSelected"),
                LocalizationManager.Get("IntegrationsImportDialogTitle"));
            return;
        }

        DialogResult = true;
    }

    private void RefreshSelectAllTexts()
    {
        SelectAllCustomersCheckBox.Content = string.Format(
            LocalizationManager.Get("IntegrationsSelectAllCustomers"),
            _preview.Contacts.Count);
        SelectAllCustomersCheckBox.IsChecked = _preview.Contacts.Count > 0 && _preview.Contacts.All(x => x.IsSelected);

        SelectAllInvoicesCheckBox.Content = string.Format(
            LocalizationManager.Get("IntegrationsSelectAllInvoices"),
            _preview.Invoices.Count);
        SelectAllInvoicesCheckBox.IsChecked = _preview.Invoices.Count > 0 && _preview.Invoices.All(x => x.IsSelected);

        SelectAllOffersCheckBox.Content = string.Format(
            LocalizationManager.Get("IntegrationsSelectAllOffers"),
            _preview.Offers.Count);
        SelectAllOffersCheckBox.IsChecked = _preview.Offers.Count > 0 && _preview.Offers.All(x => x.IsSelected);
    }
}
