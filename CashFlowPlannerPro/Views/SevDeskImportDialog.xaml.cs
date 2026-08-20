using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

internal enum SevDeskImportPhase
{
    Preparing,
    Customers,
    Invoices,
    Offers,
    Completed,
    Failed
}

internal readonly record struct SevDeskImportProgress(
    int Completed,
    int Total,
    SevDeskImportPhase Phase);

internal sealed record SevDeskImportSelection(
    IReadOnlyList<SevDeskContactPreview> Contacts,
    IReadOnlyList<SevDeskInvoicePreview> Invoices,
    IReadOnlyList<SevDeskOfferPreview> Offers)
{
    public int Total => Contacts.Count + Invoices.Count + Offers.Count;
}

public partial class SevDeskImportDialog : Window
{
    private readonly SevDeskImportPreview _preview;
    private readonly Func<SevDeskImportSelection, IProgress<SevDeskImportProgress>, Task> _runImportAsync;
    private bool _isUpdatingSelectAllState;
    private bool _isImporting;
    private bool _allowClose;
    private int _lastCompleted;
    private int _lastTotal;

    internal SevDeskImportDialog(
        SevDeskImportPreview preview,
        Func<SevDeskImportSelection, IProgress<SevDeskImportProgress>, Task> runImportAsync)
    {
        InitializeComponent();
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _runImportAsync = runImportAsync ?? throw new ArgumentNullException(nameof(runImportAsync));
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
        CustomerNumberColumn.Header = LocalizationManager.Get("IntegrationsCustomerNumber");
        CustomerNameColumn.Header = LocalizationManager.Get("IntegrationsCustomers");
        CustomerStateColumn.Header = LocalizationManager.Get("IntegrationsState");
        InvoiceNumberColumn.Header = LocalizationManager.Get("IntegrationsInvoiceNumber");
        InvoiceCurrencyColumn.Header = LocalizationManager.Get("IntegrationsCurrency");
        InvoiceSourceStatusColumn.Header = LocalizationManager.Get("IntegrationsSourceStatus");
        InvoiceCustomerColumn.Header = LocalizationManager.Get("IntegrationsCustomers");
        InvoiceDateColumn.Header = LocalizationManager.Get("IntegrationsDate");
        InvoiceDueDateColumn.Header = LocalizationManager.Get("IntegrationsDueDate");
        InvoiceAmountColumn.Header = LocalizationManager.Get("IntegrationsAmount");
        InvoiceStateColumn.Header = LocalizationManager.Get("IntegrationsImportState");
        OfferNumberColumn.Header = LocalizationManager.Get("IntegrationsOfferNumber");
        OfferCurrencyColumn.Header = LocalizationManager.Get("IntegrationsCurrency");
        OfferSourceStatusColumn.Header = LocalizationManager.Get("IntegrationsSourceStatus");
        OfferCustomerColumn.Header = LocalizationManager.Get("IntegrationsCustomers");
        OfferDateColumn.Header = LocalizationManager.Get("IntegrationsDate");
        OfferExpectedDateColumn.Header = LocalizationManager.Get("IntegrationsExpectedDate");
        OfferAmountColumn.Header = LocalizationManager.Get("IntegrationsAmount");
        OfferProbabilityColumn.Header = LocalizationManager.Get("IntegrationsProbability");
        OfferStateColumn.Header = LocalizationManager.Get("IntegrationsImportState");
        CancelButton.Content = LocalizationManager.Get("Cancel");
        ImportButton.Content = LocalizationManager.Get("IntegrationsImportAction");
        ImportProgressPhaseText.Text = LocalizationManager.Get("IntegrationsImportProgressPreparing");
    }

    private void SelectAllCustomersCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelectAllState)
            return;

        bool isChecked = SelectAllCustomersCheckBox.IsChecked == true;
        foreach (var item in _preview.Contacts)
            item.IsSelected = item.CanImport && isChecked;
        CustomersGrid.Items.Refresh();
        RefreshSelectAllTexts();
    }

    private void SelectAllInvoicesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelectAllState)
            return;

        bool isChecked = SelectAllInvoicesCheckBox.IsChecked == true;
        foreach (var item in _preview.Invoices)
            item.IsSelected = item.CanImport && isChecked;
        InvoicesGrid.Items.Refresh();
        RefreshSelectAllTexts();
    }

    private void SelectAllOffersCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelectAllState)
            return;

        bool isChecked = SelectAllOffersCheckBox.IsChecked == true;
        foreach (var item in _preview.Offers)
            item.IsSelected = item.CanImport && isChecked;
        OffersGrid.Items.Refresh();
        RefreshSelectAllTexts();
    }

    private void GridSelectionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            switch (checkBox.DataContext)
            {
                case SevDeskContactPreview contact:
                    contact.IsSelected = contact.CanImport && checkBox.IsChecked == true;
                    break;
                case SevDeskInvoicePreview invoice:
                    invoice.IsSelected = invoice.CanImport && checkBox.IsChecked == true;
                    break;
                case SevDeskOfferPreview offer:
                    offer.IsSelected = offer.CanImport && checkBox.IsChecked == true;
                    break;
            }
        }

        RefreshSelectAllTexts();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isImporting)
            return;

        DialogResult = false;
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isImporting)
            return;

        var selection = new SevDeskImportSelection(
            _preview.Contacts.Where(x => x.IsSelected && x.CanImport).ToList(),
            _preview.Invoices.Where(x => x.IsSelected && x.CanImport).ToList(),
            _preview.Offers.Where(x => x.IsSelected && x.CanImport).ToList());

        if (selection.Total == 0)
        {
            ModernMessageBox.ShowError(
                LocalizationManager.Get("IntegrationsNothingSelected"),
                LocalizationManager.Get("IntegrationsImportDialogTitle"));
            return;
        }

        LastImportError = null;
        SetImporting(true, selection.Total);
        var progress = new Progress<SevDeskImportProgress>(UpdateProgress);

        try
        {
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            await _runImportAsync(selection, progress);
            UpdateProgress(new SevDeskImportProgress(selection.Total, selection.Total, SevDeskImportPhase.Completed));
            _isImporting = false;
            _allowClose = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            var reference = AppLogger.LogException(
                "sevdesk.import.failed",
                ex,
                new { provider = "sevDesk", selectedCount = selection.Total });
            AppLogger.Audit(
                "sevdesk.import.completed",
                "sevDesk",
                success: false,
                new { reference, selectedCount = selection.Total });
            LastImportError = string.Format(
                LocalizationManager.Get("ErrorReferenceValue"),
                reference);
            UpdateProgress(new SevDeskImportProgress(_lastCompleted, _lastTotal, SevDeskImportPhase.Failed));
            SetImporting(false, _lastTotal);
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("IntegrationsImportFailed"), LastImportError),
                LocalizationManager.Get("IntegrationsImportDialogTitle"));
        }
    }

    internal string? LastImportError { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isImporting && !_allowClose)
            e.Cancel = true;

        base.OnClosing(e);
    }

    private void SetImporting(bool isImporting, int total)
    {
        _isImporting = isImporting;
        SelectionContent.IsEnabled = !isImporting;
        ImportButton.IsEnabled = !isImporting;
        CancelButton.IsEnabled = !isImporting;
        ImportProgressPanel.Visibility = Visibility.Visible;

        if (isImporting)
        {
            _lastCompleted = 0;
            _lastTotal = total;
            UpdateProgress(new SevDeskImportProgress(0, total, SevDeskImportPhase.Preparing));
        }
    }

    private void UpdateProgress(SevDeskImportProgress progress)
    {
        _lastTotal = Math.Max(1, progress.Total);
        _lastCompleted = Math.Clamp(progress.Completed, 0, _lastTotal);
        ImportProgressBar.Maximum = _lastTotal;
        ImportProgressBar.Value = _lastCompleted;
        ImportProgressCountText.Text = string.Format(
            LocalizationManager.Get("IntegrationsImportProgressCount"),
            _lastCompleted,
            _lastTotal);
        ImportProgressPhaseText.Text = LocalizationManager.Get(progress.Phase switch
        {
            SevDeskImportPhase.Customers => "IntegrationsImportProgressCustomers",
            SevDeskImportPhase.Invoices => "IntegrationsImportProgressInvoices",
            SevDeskImportPhase.Offers => "IntegrationsImportProgressOffers",
            SevDeskImportPhase.Completed => "IntegrationsImportProgressCompleted",
            SevDeskImportPhase.Failed => "IntegrationsImportProgressFailed",
            _ => "IntegrationsImportProgressPreparing"
        });
    }

    private void RefreshSelectAllTexts()
    {
        _isUpdatingSelectAllState = true;

        SelectAllCustomersCheckBox.Content = string.Format(
            LocalizationManager.Get("IntegrationsSelectAllCustomers"),
            _preview.Contacts.Count(x => x.CanImport));
        SelectAllCustomersCheckBox.IsChecked = GetSelectAllState(
            _preview.Contacts.Where(x => x.CanImport).Select(x => x.IsSelected));

        SelectAllInvoicesCheckBox.Content = string.Format(
            LocalizationManager.Get("IntegrationsSelectAllInvoices"),
            _preview.Invoices.Count(x => x.CanImport));
        SelectAllInvoicesCheckBox.IsChecked = GetSelectAllState(_preview.Invoices.Where(x => x.CanImport).Select(x => x.IsSelected));

        SelectAllOffersCheckBox.Content = string.Format(
            LocalizationManager.Get("IntegrationsSelectAllOffers"),
            _preview.Offers.Count(x => x.CanImport));
        SelectAllOffersCheckBox.IsChecked = GetSelectAllState(_preview.Offers.Where(x => x.CanImport).Select(x => x.IsSelected));

        _isUpdatingSelectAllState = false;
    }

    private static bool? GetSelectAllState(IEnumerable<bool> selections)
    {
        var selectionList = selections.ToList();
        if (selectionList.Count == 0)
            return false;

        if (selectionList.All(x => x))
            return true;

        if (selectionList.All(x => !x))
            return false;

        return null;
    }
}
