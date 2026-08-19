using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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

    private readonly record struct ImportRunStats(
        ImportStats Customers,
        ImportStats Invoices,
        ImportStats Offers);

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

            ImportRunStats? importStats = null;
            var dialog = new SevDeskImportDialog(
                preview,
                async (selection, progress) =>
                {
                    importStats = await ImportSelectionAsync(selection, progress);
                })
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                var completedStats = importStats
                    ?? throw new InvalidOperationException("Der sevDesk-Import wurde ohne Ergebnis beendet.");

                ModernMessageBox.Show(
                    string.Format(
                        LocalizationManager.Get("IntegrationsImportSuccess"),
                        completedStats.Customers.SelectedCount,
                        completedStats.Customers.ChangedCount,
                        completedStats.Invoices.SelectedCount,
                        completedStats.Invoices.ChangedCount,
                        completedStats.Offers.SelectedCount,
                        completedStats.Offers.ChangedCount),
                    LocalizationManager.Get("IntegrationsImportDialogTitle"));
                SetStatus(LocalizationManager.Get("IntegrationsImportCompleted"), Brushes.SeaGreen);
            }
            else if (!string.IsNullOrWhiteSpace(dialog.LastImportError))
            {
                SetStatus(
                    string.Format(LocalizationManager.Get("IntegrationsImportFailed"), dialog.LastImportError),
                    Brushes.IndianRed);
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

    private static void PreparePreview(SevDeskImportPreview preview)
    {
        var localCustomers = Database.Instance.GetCustomers();
        ResolveCustomerBatch(preview.Contacts, localCustomers, initializeSelection: true);

        var localInvoices = Database.Instance.GetInvoices();
        foreach (var invoice in preview.Invoices)
        {
            invoice.ExistsLocally = FindExistingInvoice(localInvoices, invoice, out var hasConflict) != null;
            invoice.HasImportConflict = hasConflict;
            invoice.ImportState = !invoice.IsCurrencySupported
                ? string.Format(LocalizationManager.Get("IntegrationsUnsupportedCurrency"), invoice.CurrencyDisplay)
                : LocalizationManager.Get(
                    hasConflict ? "IntegrationsImportConflict" : invoice.ExistsLocally ? "IntegrationsExisting" : "IntegrationsNew");
            invoice.IsSelected = invoice.CanImport && !invoice.ExistsLocally && !invoice.IsCancelled;
        }

        var localOffers = Database.Instance.GetOffers();
        foreach (var offer in preview.Offers)
        {
            offer.ExistsLocally = FindExistingOffer(localOffers, offer, out var hasConflict) != null;
            offer.HasImportConflict = hasConflict;
            offer.ImportState = !offer.IsCurrencySupported
                ? string.Format(LocalizationManager.Get("IntegrationsUnsupportedCurrency"), offer.CurrencyDisplay)
                : LocalizationManager.Get(
                    hasConflict ? "IntegrationsImportConflict" : offer.ExistsLocally ? "IntegrationsExisting" : "IntegrationsNew");
            offer.IsSelected = offer.CanImport && !offer.ExistsLocally && !offer.IsRejected;
        }
    }

    private static bool CustomerExists(IEnumerable<Customer> localCustomers, SevDeskContactPreview contact)
    {
        return FindExistingCustomer(localCustomers, contact, out _) != null;
    }

    private static Customer? FindExistingCustomer(IEnumerable<Customer> localCustomers, SevDeskContactPreview contact)
        => FindExistingCustomer(localCustomers, contact, out _);

    private static Customer? FindExistingCustomer(
        IEnumerable<Customer> localCustomers,
        SevDeskContactPreview contact,
        out bool hasConflict)
        => FindExistingCustomer(localCustomers, contact, out hasConflict, out _);

    private static Customer? FindExistingCustomer(
        IEnumerable<Customer> localCustomers,
        SevDeskContactPreview contact,
        out bool hasConflict,
        out string? conflictResourceKey)
        => FindExistingCustomer(
            localCustomers,
            contact,
            out hasConflict,
            out conflictResourceKey,
            out _);

    private static Customer? FindExistingCustomer(
        IEnumerable<Customer> localCustomers,
        SevDeskContactPreview contact,
        out bool hasConflict,
        out string? conflictResourceKey,
        out Customer? conflictedNaturalMatch)
    {
        hasConflict = false;
        conflictResourceKey = null;
        conflictedNaturalMatch = null;

        if (string.IsNullOrWhiteSpace(contact.ExternalId)
            || NormalizeCustomerNumber(contact.CustomerNumber).Length > SevDeskContactPreview.CustomerNumberMaxLength)
        {
            hasConflict = true;
            return null;
        }

        var candidates = localCustomers.ToList();

        var sourceMatches = candidates.Where(local =>
                ContainsSevDeskMarker(local.Notes, contact.ExternalId))
            .Take(2)
            .ToList();
        if (sourceMatches.Count == 1)
            return sourceMatches[0];
        if (sourceMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contact.CustomerNumber))
        {
            var normalizedCustomerNumber = contact.CustomerNumber.Trim();
            var customerNumberMatches = candidates.Where(local =>
                    !string.IsNullOrWhiteSpace(local.CustomerNumber)
                    && string.Equals(
                        local.CustomerNumber.Trim(),
                        normalizedCustomerNumber,
                        StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (customerNumberMatches.Count == 1)
            {
                if (ContainsAnySevDeskMarker(customerNumberMatches[0].Notes))
                {
                    hasConflict = true;
                    conflictResourceKey = "IntegrationsCustomerForeignSourceMatch";
                    conflictedNaturalMatch = customerNumberMatches[0];
                    return null;
                }

                return customerNumberMatches[0];
            }
            if (customerNumberMatches.Count > 1)
            {
                hasConflict = true;
                return null;
            }
        }

        var fallbackMatches = candidates.Where(local =>
                (!string.IsNullOrWhiteSpace(contact.Email)
                    && string.Equals(local.Email, contact.Email, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(local.DisplayName, contact.DisplayName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(local.City, contact.City, StringComparison.OrdinalIgnoreCase)))
            .Take(2)
            .ToList();
        if (fallbackMatches.Count == 1)
        {
            if (ContainsAnySevDeskMarker(fallbackMatches[0].Notes))
            {
                hasConflict = true;
                conflictResourceKey = "IntegrationsCustomerForeignSourceMatch";
                conflictedNaturalMatch = fallbackMatches[0];
                return null;
            }

            return fallbackMatches[0];
        }

        hasConflict = fallbackMatches.Count > 1;
        return null;
    }

    private static IReadOnlyDictionary<SevDeskContactPreview, Customer?> ResolveCustomerBatch(
        IEnumerable<SevDeskContactPreview> contacts,
        IEnumerable<Customer> localCustomers,
        bool initializeSelection)
    {
        var contactList = contacts.ToList();
        var localCustomerList = localCustomers.ToList();
        var resolutions = new Dictionary<SevDeskContactPreview, Customer?>();
        var localReservations = new Dictionary<SevDeskContactPreview, Customer?>();

        foreach (var contact in contactList)
        {
            contact.HasImportConflict = false;
            contact.ExistsLocally = false;
            resolutions[contact] = null;
            localReservations[contact] = null;
        }

        void MarkConflict(SevDeskContactPreview contact, string resourceKey)
        {
            if (!contact.HasImportConflict)
                contact.ImportState = LocalizationManager.Get(resourceKey);

            contact.HasImportConflict = true;
            contact.IsSelected = false;
        }

        foreach (var contact in contactList)
        {
            if (string.IsNullOrWhiteSpace(contact.ExternalId))
                MarkConflict(contact, "IntegrationsCustomerMissingExternalId");

            if (NormalizeCustomerNumber(contact.CustomerNumber).Length > SevDeskContactPreview.CustomerNumberMaxLength)
                MarkConflict(contact, "IntegrationsCustomerNumberTooLong");
        }

        foreach (var duplicateExternalIdGroup in contactList
                     .Where(contact => !string.IsNullOrWhiteSpace(contact.ExternalId))
                     .GroupBy(contact => contact.ExternalId.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Skip(1).Any()))
        {
            foreach (var contact in duplicateExternalIdGroup)
                MarkConflict(contact, "IntegrationsCustomerDuplicateExternalId");
        }

        foreach (var duplicateCustomerNumberGroup in contactList
                     .Where(contact => !string.IsNullOrWhiteSpace(contact.CustomerNumber))
                     .GroupBy(contact => NormalizeCustomerNumber(contact.CustomerNumber), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Skip(1).Any()))
        {
            foreach (var contact in duplicateCustomerNumberGroup)
                MarkConflict(contact, "IntegrationsCustomerDuplicateNumber");
        }

        foreach (var contact in contactList)
        {
            var match = FindExistingCustomer(
                localCustomerList,
                contact,
                out var hasLocalConflict,
                out var localConflictResourceKey,
                out var conflictedNaturalMatch);
            resolutions[contact] = match;
            localReservations[contact] = match ?? conflictedNaturalMatch;
            contact.ExistsLocally = localReservations[contact] != null;

            if (hasLocalConflict && !contact.HasImportConflict)
                MarkConflict(contact, localConflictResourceKey ?? "IntegrationsImportConflict");
        }

        var sharedLocalMatches = contactList
            .Select(contact => (Contact: contact, Match: localReservations[contact]))
            .Where(resolution => resolution.Match != null)
            .GroupBy(resolution => resolution.Match)
            .Where(group => group.Skip(1).Any());

        foreach (var sharedLocalMatch in sharedLocalMatches)
        {
            foreach (var resolution in sharedLocalMatch)
                MarkConflict(resolution.Contact, "IntegrationsCustomerSharedLocalMatch");
        }

        foreach (var contact in contactList)
        {
            if (!contact.HasImportConflict)
            {
                contact.ImportState = LocalizationManager.Get(
                    contact.ExistsLocally ? "IntegrationsExisting" : "IntegrationsNew");
            }

            if (initializeSelection)
                contact.IsSelected = contact.CanImport && !contact.ExistsLocally;
        }

        return resolutions;
    }

    private static string NormalizeCustomerNumber(string? customerNumber) =>
        (customerNumber ?? "").Trim();

    private static bool InvoiceExists(IEnumerable<Invoice> localInvoices, SevDeskInvoicePreview invoice)
    {
        return FindExistingInvoice(localInvoices, invoice) != null;
    }

    private static Invoice? FindExistingInvoice(IEnumerable<Invoice> localInvoices, SevDeskInvoicePreview invoice)
        => FindExistingInvoice(localInvoices, invoice, out _);

    private static Invoice? FindExistingInvoice(
        IEnumerable<Invoice> localInvoices,
        SevDeskInvoicePreview invoice,
        out bool hasConflict)
    {
        hasConflict = false;
        var candidates = localInvoices.ToList();
        var sourceMatches = candidates.Where(local =>
            DocumentContentMerge.HasExactSource(local.Content, "sevDesk", "Invoice", invoice.ExternalId))
            .Take(2)
            .ToList();
        if (sourceMatches.Count == 1)
            return sourceMatches[0];
        if (sourceMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        var legacyMarkerMatches = candidates.Where(local =>
            !DocumentContentMerge.HasSourceIdentity(local.Content)
            && ContainsSevDeskMarker(local.Description, invoice.ExternalId))
            .Take(2)
            .ToList();
        if (legacyMarkerMatches.Count == 1)
            return legacyMarkerMatches[0];
        if (legacyMarkerMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        var invoiceNumberMatches = candidates.Where(local =>
                !DocumentContentMerge.HasSourceIdentity(local.Content)
                && !string.IsNullOrWhiteSpace(invoice.InvoiceNumber)
                && string.Equals(local.InvoiceNumber, invoice.InvoiceNumber, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        if (invoiceNumberMatches.Count == 1)
            return invoiceNumberMatches[0];
        if (invoiceNumberMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        var legacyNumberMatches = candidates.Where(local =>
                !DocumentContentMerge.HasSourceIdentity(local.Content)
                && string.IsNullOrWhiteSpace(local.InvoiceNumber)
                && ContainsExactTextToken(local.Description, invoice.InvoiceNumber))
            .Take(2)
            .ToList();
        if (legacyNumberMatches.Count == 1)
            return legacyNumberMatches[0];
        if (legacyNumberMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        var naturalMatches = candidates.Where(local =>
                !DocumentContentMerge.HasSourceIdentity(local.Content)
                && string.Equals(local.Customer, invoice.CustomerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(local.IssueDate, invoice.IssueDate, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(local.Amount - invoice.Amount) < 0.01)
            .Take(2)
            .ToList();
        if (naturalMatches.Count == 1)
            return naturalMatches[0];

        hasConflict = naturalMatches.Count > 1;
        return null;
    }

    private static bool OfferExists(IEnumerable<Offer> localOffers, SevDeskOfferPreview offer)
    {
        return FindExistingOffer(localOffers, offer) != null;
    }

    private static Offer? FindExistingOffer(IEnumerable<Offer> localOffers, SevDeskOfferPreview offer)
        => FindExistingOffer(localOffers, offer, out _);

    private static Offer? FindExistingOffer(
        IEnumerable<Offer> localOffers,
        SevDeskOfferPreview offer,
        out bool hasConflict)
    {
        hasConflict = false;
        var candidates = localOffers.ToList();
        var sourceMatches = candidates.Where(local =>
            DocumentContentMerge.HasExactSource(local.Content, "sevDesk", "Order", offer.ExternalId))
            .Take(2)
            .ToList();
        if (sourceMatches.Count == 1)
            return sourceMatches[0];
        if (sourceMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        var legacyMarkerMatches = candidates.Where(local =>
            !DocumentContentMerge.HasSourceIdentity(local.Content)
            && ContainsSevDeskMarker(local.Description, offer.ExternalId))
            .Take(2)
            .ToList();
        if (legacyMarkerMatches.Count == 1)
            return legacyMarkerMatches[0];
        if (legacyMarkerMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        var offerNumberMatches = candidates.Where(local =>
                !DocumentContentMerge.HasSourceIdentity(local.Content)
                && !string.IsNullOrWhiteSpace(offer.OfferNumber)
                && string.Equals(local.OfferNumber, offer.OfferNumber, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        if (offerNumberMatches.Count == 1)
            return offerNumberMatches[0];
        if (offerNumberMatches.Count > 1)
        {
            hasConflict = true;
            return null;
        }

        var naturalMatches = candidates.Where(local =>
                !DocumentContentMerge.HasSourceIdentity(local.Content)
                && string.Equals(local.Customer, offer.CustomerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(local.OfferDate, offer.OfferDate, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(local.Amount - offer.Amount) < 0.01)
            .Take(2)
            .ToList();
        if (naturalMatches.Count == 1)
            return naturalMatches[0];

        hasConflict = naturalMatches.Count > 1;
        return null;
    }

    private static async Task<ImportRunStats> ImportSelectionAsync(
        SevDeskImportSelection selection,
        IProgress<SevDeskImportProgress> progress)
    {
        var completed = 0;
        var total = selection.Total;
        progress.Report(new SevDeskImportProgress(completed, total, SevDeskImportPhase.Preparing));
        await Dispatcher.Yield(DispatcherPriority.Render);

        async Task ReportItemProcessedAsync(SevDeskImportPhase phase)
        {
            completed++;
            progress.Report(new SevDeskImportProgress(completed, total, phase));
            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        var customerStats = new ImportStats(0, 0, 0);
        if (selection.Contacts.Count > 0)
        {
            progress.Report(new SevDeskImportProgress(completed, total, SevDeskImportPhase.Customers));
            await Dispatcher.Yield(DispatcherPriority.Render);
            customerStats = await ImportCustomersAsync(
                selection.Contacts,
                () => ReportItemProcessedAsync(SevDeskImportPhase.Customers));
        }

        var invoiceStats = new ImportStats(0, 0, 0);
        if (selection.Invoices.Count > 0)
        {
            progress.Report(new SevDeskImportProgress(completed, total, SevDeskImportPhase.Invoices));
            await Dispatcher.Yield(DispatcherPriority.Render);
            invoiceStats = await ImportInvoicesAsync(
                selection.Invoices,
                () => ReportItemProcessedAsync(SevDeskImportPhase.Invoices));
        }

        var offerStats = new ImportStats(0, 0, 0);
        if (selection.Offers.Count > 0)
        {
            progress.Report(new SevDeskImportProgress(completed, total, SevDeskImportPhase.Offers));
            await Dispatcher.Yield(DispatcherPriority.Render);
            offerStats = await ImportOffersAsync(
                selection.Offers,
                () => ReportItemProcessedAsync(SevDeskImportPhase.Offers));
        }

        return new ImportRunStats(customerStats, invoiceStats, offerStats);
    }

    private static async Task<ImportStats> ImportCustomersAsync(
        IReadOnlyList<SevDeskContactPreview> selectedList,
        Func<Task> onItemProcessedAsync)
    {
        int addedCustomers = 0;
        int updatedCustomers = 0;
        var existing = Database.Instance.GetCustomers();
        var resolutions = ResolveCustomerBatch(selectedList, existing, initializeSelection: false);
        foreach (var contact in selectedList)
        {
            if (contact.HasImportConflict)
            {
                contact.IsSelected = false;
            }
            else if (resolutions[contact] is Customer existingCustomer)
            {
                if (MergeCustomer(existingCustomer, contact))
                {
                    Database.Instance.UpdateCustomer(existingCustomer);
                    updatedCustomers++;
                }
            }
            else
            {
                var customer = contact.ToCustomer();
                Database.Instance.AddCustomer(customer);
                addedCustomers++;
            }

            await onItemProcessedAsync();
        }

        return new ImportStats(selectedList.Count, addedCustomers, updatedCustomers);
    }

    private static async Task<ImportStats> ImportInvoicesAsync(
        IReadOnlyList<SevDeskInvoicePreview> selectedList,
        Func<Task> onItemProcessedAsync)
    {
        int addedInvoices = 0;
        int updatedInvoices = 0;
        var existing = Database.Instance.GetInvoices();
        foreach (var invoice in selectedList)
        {
            var existingInvoice = FindExistingInvoice(existing, invoice, out var hasConflict);
            if (hasConflict)
            {
                invoice.HasImportConflict = true;
                invoice.IsSelected = false;
                invoice.ImportState = LocalizationManager.Get("IntegrationsImportConflict");
            }
            else if (existingInvoice != null)
            {
                if (MergeInvoice(existingInvoice, invoice))
                {
                    Database.Instance.UpdateInvoice(existingInvoice);
                    updatedInvoices++;
                }
            }
            else
            {
                var localInvoice = invoice.ToInvoice();
                Database.Instance.AddInvoice(localInvoice);
                existing.Add(localInvoice);
                addedInvoices++;
            }

            await onItemProcessedAsync();
        }

        return new ImportStats(selectedList.Count, addedInvoices, updatedInvoices);
    }

    private static async Task<ImportStats> ImportOffersAsync(
        IReadOnlyList<SevDeskOfferPreview> selectedList,
        Func<Task> onItemProcessedAsync)
    {
        int addedOffers = 0;
        int updatedOffers = 0;
        var existing = Database.Instance.GetOffers();
        foreach (var offer in selectedList)
        {
            var existingOffer = FindExistingOffer(existing, offer, out var hasConflict);
            if (hasConflict)
            {
                offer.HasImportConflict = true;
                offer.IsSelected = false;
                offer.ImportState = LocalizationManager.Get("IntegrationsImportConflict");
            }
            else if (existingOffer != null)
            {
                if (MergeOffer(existingOffer, offer))
                {
                    Database.Instance.UpdateOffer(existingOffer);
                    updatedOffers++;
                }
            }
            else
            {
                var localOffer = offer.ToOffer();
                Database.Instance.AddOffer(localOffer);
                existing.Add(localOffer);
                addedOffers++;
            }

            await onItemProcessedAsync();
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

        changed |= ApplyImportedCustomerNumber(contact.CustomerNumber, isLinkedSevDeskCustomer, () => existingCustomer.CustomerNumber, value => existingCustomer.CustomerNumber = value);
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
        bool isLinkedSevDeskInvoice =
            DocumentContentMerge.HasExactSource(existingInvoice.Content, "sevDesk", "Invoice", invoice.ExternalId)
            || (!DocumentContentMerge.HasSourceIdentity(existingInvoice.Content)
                && ContainsSevDeskMarker(existingInvoice.Description, invoice.ExternalId));

        changed |= ApplyImportedValue(invoice.InvoiceNumber, isLinkedSevDeskInvoice, () => existingInvoice.InvoiceNumber, value => existingInvoice.InvoiceNumber = value);
        changed |= ApplyImportedValue(invoice.CustomerName, isLinkedSevDeskInvoice, () => existingInvoice.Customer, value => existingInvoice.Customer = value);
        changed |= ApplyImportedValue(invoice.IssueDate, isLinkedSevDeskInvoice, () => existingInvoice.IssueDate, value => existingInvoice.IssueDate = value);
        changed |= ApplyImportedValue(invoice.DueDate, isLinkedSevDeskInvoice, () => existingInvoice.DueDate, value => existingInvoice.DueDate = value);
        changed |= ApplyImportedNumericValue(invoice.Amount, isLinkedSevDeskInvoice, () => existingInvoice.Amount, value => existingInvoice.Amount = value);
        changed |= ApplyImportedNumericValue(invoice.NetAmount, isLinkedSevDeskInvoice, () => existingInvoice.NetAmount, value => existingInvoice.NetAmount = value);
        changed |= ApplyImportedNumericValue(invoice.VatAmount, isLinkedSevDeskInvoice, () => existingInvoice.VatAmount, value => existingInvoice.VatAmount = value);
        changed |= ApplyImportedNumericValue(invoice.VatRate, isLinkedSevDeskInvoice, () => existingInvoice.VatRate, value => existingInvoice.VatRate = value);
        changed |= ApplyImportedValue(invoice.Status, isLinkedSevDeskInvoice, () => existingInvoice.Status, value => existingInvoice.Status = value);
        changed |= ApplyImportedNumericValue(invoice.PaidAmount, isLinkedSevDeskInvoice, () => existingInvoice.PaidAmount, value => existingInvoice.PaidAmount = value);
        changed |= ApplyImportedNullableValue(invoice.PaidDate, isLinkedSevDeskInvoice, () => existingInvoice.PaidDate, value => existingInvoice.PaidDate = value);

        if (!isLinkedSevDeskInvoice)
        {
            // Description is deliberately not source-owned. It is initialized
            // for a natural-key adoption only when the local field is empty.
            changed |= ApplyImportedValue(
                invoice.Description,
                false,
                () => existingInvoice.Description,
                value => existingInvoice.Description = value);
        }

        existingInvoice.Content = DocumentContentMerge.MergeImported(existingInvoice.Content, invoice.Content);
        changed = true;

        return changed;
    }

    private static bool MergeOffer(Offer existingOffer, SevDeskOfferPreview offer)
    {
        bool changed = false;
        bool isLinkedSevDeskOffer =
            DocumentContentMerge.HasExactSource(existingOffer.Content, "sevDesk", "Order", offer.ExternalId)
            || (!DocumentContentMerge.HasSourceIdentity(existingOffer.Content)
                && ContainsSevDeskMarker(existingOffer.Description, offer.ExternalId));

        changed |= ApplyImportedValue(offer.OfferNumber, isLinkedSevDeskOffer, () => existingOffer.OfferNumber, value => existingOffer.OfferNumber = value);
        changed |= ApplyImportedValue(offer.CustomerName, isLinkedSevDeskOffer, () => existingOffer.Customer, value => existingOffer.Customer = value);
        changed |= ApplyImportedValue(offer.OfferDate, isLinkedSevDeskOffer, () => existingOffer.OfferDate, value => existingOffer.OfferDate = value);
        changed |= ApplyImportedValue(offer.DateExpected, isLinkedSevDeskOffer, () => existingOffer.DateExpected, value => existingOffer.DateExpected = value);
        changed |= ApplyImportedNumericValue(offer.AmountBeforeDiscount, isLinkedSevDeskOffer, () => existingOffer.AmountBeforeDiscount, value => existingOffer.AmountBeforeDiscount = value);
        changed |= ApplyImportedNumericValue(offer.DiscountPercent, isLinkedSevDeskOffer, () => existingOffer.DiscountPercent, value => existingOffer.DiscountPercent = value);
        changed |= ApplyImportedNumericValue(offer.Amount, isLinkedSevDeskOffer, () => existingOffer.Amount, value => existingOffer.Amount = value);
        changed |= ApplyImportedNumericValue(offer.Probability, isLinkedSevDeskOffer, () => existingOffer.Probability, value => existingOffer.Probability = value);
        changed |= ApplyImportedValue(offer.Status, isLinkedSevDeskOffer, () => existingOffer.Status, value => existingOffer.Status = value);
        if (offer.PaymentDelay.HasValue)
        {
            changed |= ApplyImportedIntValue(
                offer.PaymentDelay.Value,
                isLinkedSevDeskOffer,
                () => existingOffer.PaymentDelay,
                value => existingOffer.PaymentDelay = value);
        }

        if (!isLinkedSevDeskOffer)
        {
            changed |= ApplyImportedValue(
                offer.Description,
                false,
                () => existingOffer.Description,
                value => existingOffer.Description = value);
        }

        existingOffer.Content = DocumentContentMerge.MergeImported(existingOffer.Content, offer.Content);
        changed = true;

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
        if (!double.IsFinite(importedValue))
            return false;

        var currentValue = getCurrentValue();
        if (overwriteExisting)
        {
            if (Math.Abs(currentValue - importedValue) <= 0.000001)
                return false;

            setCurrentValue(importedValue);
            return true;
        }

        if (Math.Abs(currentValue) > 0.000001 || Math.Abs(currentValue - importedValue) <= 0.000001)
            return false;

        setCurrentValue(importedValue);
        return true;
    }

    private static bool ApplyImportedIntValue(int importedValue, bool overwriteExisting, Func<int> getCurrentValue, Action<int> setCurrentValue)
    {
        if (importedValue < 0)
            return false;

        var currentValue = getCurrentValue();
        if (overwriteExisting)
        {
            if (currentValue == importedValue)
                return false;

            setCurrentValue(importedValue);
            return true;
        }

        if (currentValue != 0 || currentValue == importedValue)
            return false;

        setCurrentValue(importedValue);
        return true;
    }

    private static bool ApplyImportedNullableValue(
        string? importedValue,
        bool overwriteExisting,
        Func<string?> getCurrentValue,
        Action<string?> setCurrentValue)
    {
        var normalizedImportedValue = string.IsNullOrWhiteSpace(importedValue) ? null : importedValue.Trim();
        var rawCurrentValue = getCurrentValue();
        var currentValue = string.IsNullOrWhiteSpace(rawCurrentValue) ? null : rawCurrentValue.Trim();

        if (overwriteExisting)
        {
            if (string.Equals(currentValue, normalizedImportedValue, StringComparison.Ordinal))
                return false;

            setCurrentValue(normalizedImportedValue);
            return true;
        }

        if (normalizedImportedValue == null || currentValue != null)
            return false;

        setCurrentValue(normalizedImportedValue);
        return true;
    }

    private static bool ApplyImportedCustomerNumber(
        string importedValue,
        bool overwriteExisting,
        Func<string> getCurrentValue,
        Action<string> setCurrentValue)
    {
        var normalizedImportedValue = NormalizeCustomerNumber(importedValue);
        var currentValue = NormalizeCustomerNumber(getCurrentValue());

        if (overwriteExisting)
        {
            if (string.Equals(currentValue, normalizedImportedValue, StringComparison.Ordinal))
                return false;

            setCurrentValue(normalizedImportedValue);
            return true;
        }

        if (string.IsNullOrWhiteSpace(normalizedImportedValue)
            || !string.IsNullOrWhiteSpace(currentValue))
        {
            return false;
        }

        setCurrentValue(normalizedImportedValue);
        return true;
    }

    private static bool ContainsSevDeskMarker(string? notes, string externalId)
    {
        if (string.IsNullOrWhiteSpace(notes) || string.IsNullOrWhiteSpace(externalId))
            return false;

        var escapedId = Regex.Escape(externalId);
        var pattern = $@"(?:\[sevDesk:{escapedId}\]|(?<!\S)sevDesk:{escapedId}(?=\s|$))";
        return Regex.IsMatch(
            notes,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static bool ContainsAnySevDeskMarker(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return false;

        const string pattern = @"(?:\[sevDesk:[^\]\r\n]+\]|(?<!\S)sevDesk:\S+(?=\s|$))";
        return Regex.IsMatch(
            notes,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static bool ContainsExactTextToken(string? text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            return false;

        var pattern = $@"(?<!\S){Regex.Escape(token)}(?=\s|$)";
        return Regex.IsMatch(
            text,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }
}
