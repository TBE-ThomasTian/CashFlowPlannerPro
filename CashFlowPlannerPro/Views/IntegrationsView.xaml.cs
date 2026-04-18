using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class IntegrationsView : UserControl
{
    private readonly record struct ImportStats(int SelectedCount, int AddedCount, int UpdatedCount)
    {
        public int ChangedCount => AddedCount + UpdatedCount;
    }

    private bool _isBusy;

    public IntegrationsView()
    {
        InitializeComponent();
        ApplyLocalization();
        LoadSavedToken();
        ApplyPermissionState();
    }

    private void ApplyLocalization()
    {
        TitleText.Text = LocalizationManager.Get("IntegrationsTitle");
        SubtitleText.Text = LocalizationManager.Get("IntegrationsSubtitle");
        ProviderTitleText.Text = LocalizationManager.Get("IntegrationsProviderTitle");
        ProviderInfoText.Text = LocalizationManager.Get("IntegrationsProviderInfo");
        TokenLabelText.Text = LocalizationManager.Get("IntegrationsTokenLabel");
        SaveTokenButton.Content = LocalizationManager.Get("IntegrationsSaveToken");
        TestConnectionButton.Content = LocalizationManager.Get("IntegrationsTestConnection");
        LoadPreviewButton.Content = LocalizationManager.Get("IntegrationsLoadPreview");
        StatusTitleText.Text = LocalizationManager.Get("IntegrationsStatusTitle");
        WhatHappensTitleText.Text = LocalizationManager.Get("IntegrationsFlowTitle");
        WhatHappensText.Text = LocalizationManager.Get("IntegrationsFlowText");
        PreviewHintTitleText.Text = LocalizationManager.Get("IntegrationsPreviewTitle");
        PreviewHintText.Text = LocalizationManager.Get("IntegrationsPreviewHint");
        BusyText.Text = LocalizationManager.Get("LoadingPleaseWait");

        if (string.IsNullOrWhiteSpace(StatusText.Text) || StatusText.Text == "Noch keine Verbindung getestet.")
            SetStatus(LocalizationManager.Get("IntegrationsStatusIdle"), Brushes.Gray);
    }

    private void LoadSavedToken()
    {
        var secure = SevDeskSecureStore.Load();
        if (secure != null)
            TokenBox.Password = secure.ApiToken;
    }

    private void ApplyPermissionState()
    {
        bool canEdit = App.CanEdit(PageKeys.Integrations);
        TokenBox.IsEnabled = canEdit;
        SaveTokenButton.IsEnabled = canEdit;
        TestConnectionButton.IsEnabled = canEdit;
        LoadPreviewButton.IsEnabled = canEdit;
        PermissionHintText.Visibility = canEdit ? Visibility.Collapsed : Visibility.Visible;
        PermissionHintText.Text = LocalizationManager.Get("IntegrationsReadOnlyHint");
    }

    private void SaveTokenButton_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            ModernMessageBox.ShowError(LocalizationManager.Get("IntegrationsTokenRequired"), LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        SevDeskSecureStore.Save(new SevDeskSecureData { ApiToken = token });
        SetStatus(LocalizationManager.Get("IntegrationsTokenSaved"), Brushes.SeaGreen);
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            ModernMessageBox.ShowError(LocalizationManager.Get("IntegrationsTokenRequired"), LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        try
        {
            SetBusy(true);
            await SevDeskClient.TestConnectionAsync(token);
            SetStatus(LocalizationManager.Get("IntegrationsConnectionSuccess"), Brushes.SeaGreen);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(LocalizationManager.Get("IntegrationsConnectionFailed"), ex.Message), Brushes.IndianRed);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void LoadPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            ModernMessageBox.ShowError(LocalizationManager.Get("IntegrationsTokenRequired"), LocalizationManager.Get("AppErrorTitle"));
            return;
        }

        try
        {
            SetBusy(true);
            var preview = await SevDeskClient.LoadImportPreviewAsync(token);
            PreparePreview(preview);
            SetBusy(false);

            var dialog = new SevDeskImportDialog(preview)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                var selectedCustomers = preview.Contacts.Where(x => x.IsSelected).ToList();
                var selectedInvoices = preview.Invoices.Where(x => x.IsSelected).ToList();
                var selectedOffers = preview.Offers.Where(x => x.IsSelected).ToList();

                var customerStats = ImportCustomers(selectedCustomers);
                var invoiceStats = ImportInvoices(selectedInvoices);
                var offerStats = ImportOffers(selectedOffers);

                ModernMessageBox.Show(
                    string.Format(
                        LocalizationManager.Get("IntegrationsImportSuccess"),
                        customerStats.SelectedCount,
                        customerStats.ChangedCount,
                        invoiceStats.SelectedCount,
                        invoiceStats.ChangedCount,
                        offerStats.SelectedCount,
                        offerStats.ChangedCount),
                    LocalizationManager.Get("IntegrationsImportDialogTitle"));
                SetStatus(LocalizationManager.Get("IntegrationsImportCompleted"), Brushes.SeaGreen);
            }
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("IntegrationsPreviewFailed"), ex.Message),
                LocalizationManager.Get("IntegrationsImportDialogTitle"));
            SetStatus(string.Format(LocalizationManager.Get("IntegrationsPreviewFailed"), ex.Message), Brushes.IndianRed);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PreparePreview(SevDeskImportPreview preview)
    {
        var localCustomers = Database.Instance.GetCustomers();
        foreach (var contact in preview.Contacts)
        {
            contact.ExistsLocally = FindExistingCustomer(localCustomers, contact) != null;
            contact.ImportState = LocalizationManager.Get(contact.ExistsLocally ? "IntegrationsExisting" : "IntegrationsNew");
            contact.IsSelected = !contact.ExistsLocally;
        }

        var localInvoices = Database.Instance.GetInvoices();
        foreach (var invoice in preview.Invoices)
        {
            invoice.ExistsLocally = FindExistingInvoice(localInvoices, invoice) != null;
            invoice.ImportState = LocalizationManager.Get(invoice.ExistsLocally ? "IntegrationsExisting" : "IntegrationsNew");
            invoice.IsSelected = !invoice.ExistsLocally && !invoice.IsCancelled;
        }

        var localOffers = Database.Instance.GetOffers();
        foreach (var offer in preview.Offers)
        {
            offer.ExistsLocally = FindExistingOffer(localOffers, offer) != null;
            offer.ImportState = LocalizationManager.Get(offer.ExistsLocally ? "IntegrationsExisting" : "IntegrationsNew");
            offer.IsSelected = !offer.ExistsLocally && !offer.IsRejected;
        }
    }

    private static bool CustomerExists(IEnumerable<Customer> localCustomers, SevDeskContactPreview contact)
    {
        return FindExistingCustomer(localCustomers, contact) != null;
    }

    private static Customer? FindExistingCustomer(IEnumerable<Customer> localCustomers, SevDeskContactPreview contact)
    {
        return localCustomers.FirstOrDefault(local =>
            ContainsSevDeskMarker(local.Notes, contact.ExternalId)
            || (!string.IsNullOrWhiteSpace(contact.Email)
                && string.Equals(local.Email, contact.Email, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(local.DisplayName, contact.DisplayName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(local.City, contact.City, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool InvoiceExists(IEnumerable<Invoice> localInvoices, SevDeskInvoicePreview invoice)
    {
        return FindExistingInvoice(localInvoices, invoice) != null;
    }

    private static Invoice? FindExistingInvoice(IEnumerable<Invoice> localInvoices, SevDeskInvoicePreview invoice)
    {
        return localInvoices.FirstOrDefault(local =>
            ContainsSevDeskMarker(local.Description, invoice.ExternalId)
            || (!string.IsNullOrWhiteSpace(invoice.InvoiceNumber)
                && local.Description.Contains(invoice.InvoiceNumber, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(local.Customer, invoice.CustomerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(local.IssueDate, invoice.IssueDate, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(local.Amount - invoice.Amount) < 0.01));
    }

    private static bool OfferExists(IEnumerable<Offer> localOffers, SevDeskOfferPreview offer)
    {
        return FindExistingOffer(localOffers, offer) != null;
    }

    private static Offer? FindExistingOffer(IEnumerable<Offer> localOffers, SevDeskOfferPreview offer)
    {
        return localOffers.FirstOrDefault(local =>
            ContainsSevDeskMarker(local.Description, offer.ExternalId)
            || (!string.IsNullOrWhiteSpace(offer.OfferNumber)
                && string.Equals(local.OfferNumber, offer.OfferNumber, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(local.Customer, offer.CustomerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(local.OfferDate, offer.OfferDate, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(local.Amount - offer.Amount) < 0.01));
    }

    private static ImportStats ImportCustomers(IEnumerable<SevDeskContactPreview> selectedContacts)
    {
        var selectedList = selectedContacts.ToList();
        int addedCustomers = 0;
        int updatedCustomers = 0;
        var existing = Database.Instance.GetCustomers();
        foreach (var contact in selectedList)
        {
            var existingCustomer = FindExistingCustomer(existing, contact);
            if (existingCustomer != null)
            {
                if (MergeCustomer(existingCustomer, contact))
                {
                    Database.Instance.UpdateCustomer(existingCustomer);
                    updatedCustomers++;
                }

                continue;
            }

            var customer = contact.ToCustomer();
            Database.Instance.AddCustomer(customer);
            existing.Add(customer);
            addedCustomers++;
        }

        return new ImportStats(selectedList.Count, addedCustomers, updatedCustomers);
    }

    private static ImportStats ImportInvoices(IEnumerable<SevDeskInvoicePreview> selectedInvoices)
    {
        var selectedList = selectedInvoices.ToList();
        int addedInvoices = 0;
        int updatedInvoices = 0;
        var existing = Database.Instance.GetInvoices();
        foreach (var invoice in selectedList)
        {
            var existingInvoice = FindExistingInvoice(existing, invoice);
            if (existingInvoice != null)
            {
                if (MergeInvoice(existingInvoice, invoice))
                {
                    Database.Instance.UpdateInvoice(existingInvoice);
                    updatedInvoices++;
                }

                continue;
            }

            var localInvoice = invoice.ToInvoice();
            Database.Instance.AddInvoice(localInvoice);
            existing.Add(localInvoice);
            addedInvoices++;
        }

        return new ImportStats(selectedList.Count, addedInvoices, updatedInvoices);
    }

    private static ImportStats ImportOffers(IEnumerable<SevDeskOfferPreview> selectedOffers)
    {
        var selectedList = selectedOffers.ToList();
        int addedOffers = 0;
        int updatedOffers = 0;
        var existing = Database.Instance.GetOffers();
        foreach (var offer in selectedList)
        {
            var existingOffer = FindExistingOffer(existing, offer);
            if (existingOffer != null)
            {
                if (MergeOffer(existingOffer, offer))
                {
                    Database.Instance.UpdateOffer(existingOffer);
                    updatedOffers++;
                }

                continue;
            }

            var localOffer = offer.ToOffer();
            Database.Instance.AddOffer(localOffer);
            existing.Add(localOffer);
            addedOffers++;
        }

        return new ImportStats(selectedList.Count, addedOffers, updatedOffers);
    }

    private void SetStatus(string message, Brush color)
    {
        StatusText.Text = message;
        StatusText.Foreground = color;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool MergeCustomer(Customer existingCustomer, SevDeskContactPreview contact)
    {
        bool changed = false;
        bool isLinkedSevDeskCustomer = ContainsSevDeskMarker(existingCustomer.Notes, contact.ExternalId);

        changed |= ApplyImportedValue(contact.Company, isLinkedSevDeskCustomer, () => existingCustomer.Company, value => existingCustomer.Company = value);
        changed |= ApplyImportedValue(contact.ContactName, isLinkedSevDeskCustomer, () => existingCustomer.ContactName, value => existingCustomer.ContactName = value);
        changed |= ApplyImportedValue(contact.Email, isLinkedSevDeskCustomer, () => existingCustomer.Email, value => existingCustomer.Email = value);
        changed |= ApplyImportedValue(contact.Phone, isLinkedSevDeskCustomer, () => existingCustomer.Phone, value => existingCustomer.Phone = value);
        changed |= ApplyImportedValue(contact.Street, isLinkedSevDeskCustomer, () => existingCustomer.Street, value => existingCustomer.Street = value);
        changed |= ApplyImportedValue(contact.ZipCode, isLinkedSevDeskCustomer, () => existingCustomer.ZipCode, value => existingCustomer.ZipCode = value);
        changed |= ApplyImportedValue(contact.City, isLinkedSevDeskCustomer, () => existingCustomer.City, value => existingCustomer.City = value);
        changed |= ApplyImportedValue(contact.Country, isLinkedSevDeskCustomer, () => existingCustomer.Country, value => existingCustomer.Country = value);
        changed |= ApplyImportedValue(contact.TaxId, isLinkedSevDeskCustomer, () => existingCustomer.TaxId, value => existingCustomer.TaxId = value);

        var marker = $"sevDesk:{contact.ExternalId}";
        if (!ContainsSevDeskMarker(existingCustomer.Notes, contact.ExternalId))
        {
            existingCustomer.Notes = string.IsNullOrWhiteSpace(existingCustomer.Notes)
                ? marker
                : $"{existingCustomer.Notes}{Environment.NewLine}{marker}";
            changed = true;
        }

        return changed;
    }

    private static bool MergeInvoice(Invoice existingInvoice, SevDeskInvoicePreview invoice)
    {
        bool changed = false;
        bool isLinkedSevDeskInvoice = ContainsSevDeskMarker(existingInvoice.Description, invoice.ExternalId);

        changed |= ApplyImportedValue(invoice.CustomerName, isLinkedSevDeskInvoice, () => existingInvoice.Customer, value => existingInvoice.Customer = value);
        changed |= ApplyImportedValue(invoice.IssueDate, isLinkedSevDeskInvoice, () => existingInvoice.IssueDate, value => existingInvoice.IssueDate = value);
        changed |= ApplyImportedValue(invoice.DueDate, isLinkedSevDeskInvoice, () => existingInvoice.DueDate, value => existingInvoice.DueDate = value);
        changed |= ApplyImportedNumericValue(invoice.Amount, isLinkedSevDeskInvoice, () => existingInvoice.Amount, value => existingInvoice.Amount = value);
        changed |= ApplyImportedNumericValue(invoice.NetAmount, isLinkedSevDeskInvoice, () => existingInvoice.NetAmount, value => existingInvoice.NetAmount = value);
        changed |= ApplyImportedNumericValue(invoice.VatAmount, isLinkedSevDeskInvoice, () => existingInvoice.VatAmount, value => existingInvoice.VatAmount = value);
        changed |= ApplyImportedNumericValue(invoice.VatRate, isLinkedSevDeskInvoice, () => existingInvoice.VatRate, value => existingInvoice.VatRate = value);
        changed |= ApplyImportedValue(invoice.Status, isLinkedSevDeskInvoice, () => existingInvoice.Status, value => existingInvoice.Status = value);
        changed |= ApplyImportedNumericValue(invoice.Status == "Bezahlt" ? invoice.Amount : 0, isLinkedSevDeskInvoice, () => existingInvoice.PaidAmount, value => existingInvoice.PaidAmount = value);

        if (isLinkedSevDeskInvoice)
        {
            var importedInvoice = invoice.ToInvoice();
            if (!string.Equals(existingInvoice.Description, importedInvoice.Description, StringComparison.Ordinal))
            {
                existingInvoice.Description = importedInvoice.Description;
                changed = true;
            }

            if (!string.Equals(existingInvoice.PaidDate, importedInvoice.PaidDate, StringComparison.Ordinal))
            {
                existingInvoice.PaidDate = importedInvoice.PaidDate;
                changed = true;
            }
        }
        else
        {
            changed |= ApplyImportedValue(invoice.Description, false, () => existingInvoice.Description, value => existingInvoice.Description = value);
            if (!ContainsSevDeskMarker(existingInvoice.Description, invoice.ExternalId))
            {
                existingInvoice.Description = string.IsNullOrWhiteSpace(existingInvoice.Description)
                    ? $"[sevDesk:{invoice.ExternalId}]"
                    : $"{existingInvoice.Description} [sevDesk:{invoice.ExternalId}]";
                changed = true;
            }
        }

        return changed;
    }

    private static bool MergeOffer(Offer existingOffer, SevDeskOfferPreview offer)
    {
        bool changed = false;
        bool isLinkedSevDeskOffer = ContainsSevDeskMarker(existingOffer.Description, offer.ExternalId);

        changed |= ApplyImportedValue(offer.OfferNumber, isLinkedSevDeskOffer, () => existingOffer.OfferNumber, value => existingOffer.OfferNumber = value);
        changed |= ApplyImportedValue(offer.CustomerName, isLinkedSevDeskOffer, () => existingOffer.Customer, value => existingOffer.Customer = value);
        changed |= ApplyImportedValue(offer.OfferDate, isLinkedSevDeskOffer, () => existingOffer.OfferDate, value => existingOffer.OfferDate = value);
        changed |= ApplyImportedValue(offer.DateExpected, isLinkedSevDeskOffer, () => existingOffer.DateExpected, value => existingOffer.DateExpected = value);
        changed |= ApplyImportedNumericValue(offer.Amount, isLinkedSevDeskOffer, () => existingOffer.Amount, value => existingOffer.Amount = value);
        changed |= ApplyImportedNumericValue(offer.Probability, isLinkedSevDeskOffer, () => existingOffer.Probability, value => existingOffer.Probability = value);
        changed |= ApplyImportedValue(offer.Status, isLinkedSevDeskOffer, () => existingOffer.Status, value => existingOffer.Status = value);
        changed |= ApplyImportedIntValue(offer.PaymentDelay, isLinkedSevDeskOffer, () => existingOffer.PaymentDelay, value => existingOffer.PaymentDelay = value);

        if (isLinkedSevDeskOffer)
        {
            var importedOffer = offer.ToOffer();
            if (!string.Equals(existingOffer.Description, importedOffer.Description, StringComparison.Ordinal))
            {
                existingOffer.Description = importedOffer.Description;
                changed = true;
            }
        }
        else
        {
            changed |= ApplyImportedValue(offer.Description, false, () => existingOffer.Description, value => existingOffer.Description = value);
            if (!ContainsSevDeskMarker(existingOffer.Description, offer.ExternalId))
            {
                existingOffer.Description = string.IsNullOrWhiteSpace(existingOffer.Description)
                    ? $"[sevDesk:{offer.ExternalId}]"
                    : $"{existingOffer.Description} [sevDesk:{offer.ExternalId}]";
                changed = true;
            }
        }

        return changed;
    }

    private static bool ApplyImportedValue(string importedValue, bool overwriteExisting, Func<string> getCurrentValue, Action<string> setCurrentValue)
    {
        if (string.IsNullOrWhiteSpace(importedValue))
            return false;

        var normalizedImportedValue = importedValue.Trim();
        var currentValue = (getCurrentValue() ?? "").Trim();

        if (overwriteExisting)
        {
            if (string.Equals(currentValue, normalizedImportedValue, StringComparison.Ordinal))
                return false;

            setCurrentValue(normalizedImportedValue);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(currentValue))
            return false;

        setCurrentValue(normalizedImportedValue);
        return true;
    }

    private static bool ApplyImportedNumericValue(double importedValue, bool overwriteExisting, Func<double> getCurrentValue, Action<double> setCurrentValue)
    {
        if (importedValue <= 0)
            return false;

        var currentValue = getCurrentValue();
        if (overwriteExisting)
        {
            if (Math.Abs(currentValue - importedValue) < 0.01)
                return false;

            setCurrentValue(importedValue);
            return true;
        }

        if (Math.Abs(currentValue) > 0.01)
            return false;

        setCurrentValue(importedValue);
        return true;
    }

    private static bool ApplyImportedIntValue(int importedValue, bool overwriteExisting, Func<int> getCurrentValue, Action<int> setCurrentValue)
    {
        if (importedValue <= 0)
            return false;

        var currentValue = getCurrentValue();
        if (overwriteExisting)
        {
            if (currentValue == importedValue)
                return false;

            setCurrentValue(importedValue);
            return true;
        }

        if (currentValue > 0)
            return false;

        setCurrentValue(importedValue);
        return true;
    }

    private static bool ContainsSevDeskMarker(string notes, string externalId) =>
        !string.IsNullOrWhiteSpace(externalId)
        && notes.Contains($"sevDesk:{externalId}", StringComparison.OrdinalIgnoreCase);
}
