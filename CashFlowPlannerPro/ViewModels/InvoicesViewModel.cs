using System;
using System.Collections.ObjectModel;
using System.Linq;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CashFlowPlannerPro.ViewModels;

public partial class InvoicesViewModel : ObservableObject
{
    private const double DefaultVatRate = 19;
    private readonly Dictionary<Invoice, string> amountCalculationBases = new();

    [ObservableProperty] private ObservableCollection<Invoice> invoices = new();
    [ObservableProperty] private ObservableCollection<string> customerNames = new();
    [ObservableProperty] private Invoice? selectedInvoice;

    public void Load()
    {
        amountCalculationBases.Clear();
        Invoices = new ObservableCollection<Invoice>(Database.Instance.GetInvoices());
        CustomerNames = new ObservableCollection<string>(
            Database.Instance.GetCustomers()
                .Select(c => c.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name));
    }

    [RelayCommand]
    private void Add()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Invoices, "invoice.add")) return;

        var inv = new Invoice {
            IssueDate = DateTime.Today.ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),
            Status = "Offen",
            Amount = 0,
            NetAmount = 0,
            VatAmount = 0,
            VatRate = DefaultVatRate,
            Customer = CustomerNames.FirstOrDefault() ?? ""
        };
        Database.Instance.AddInvoice(inv);
        Invoices.Add(inv);
        SelectedInvoice = inv;
    }

    [RelayCommand]
    private void Delete()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Invoices, "invoice.delete")) return;
        if (SelectedInvoice == null) return;
        Database.Instance.DeleteInvoice(SelectedInvoice.Id);
        Invoices.Remove(SelectedInvoice);
    }

    public void DeleteInvoices(IEnumerable<Invoice> invoicesToDelete)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Invoices, "invoice.delete_many")) return;

        var ids = invoicesToDelete
            .Where(i => i.Id > 0)
            .Select(i => i.Id)
            .Distinct()
            .ToHashSet();

        if (ids.Count == 0)
            return;

        foreach (var id in ids)
            Database.Instance.DeleteInvoice(id);

        Invoices = new ObservableCollection<Invoice>(Invoices.Where(i => !ids.Contains(i.Id)));
        SelectedInvoice = Invoices.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectPdf()
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Invoices, "invoice.attach_pdf")) return;
        if (SelectedInvoice == null) return;
        var dlg = new OpenFileDialog { Filter = "PDF Dateien (*.pdf)|*.pdf" };
        if (dlg.ShowDialog() == true) {
            SelectedInvoice.PdfPath = dlg.FileName;
            Save(SelectedInvoice);
            // Refresh to update UI
            var idx = Invoices.IndexOf(SelectedInvoice);
            if (idx >= 0) {
                var item = Invoices[idx];
                Invoices[idx] = item;
            }
        }
    }

    [RelayCommand]
    private void OpenPdf()
    {
        if (!SafeDocumentLauncher.TryOpenLocalPdf(SelectedInvoice?.PdfPath, out var error))
            ModernMessageBox.ShowError(error, LocalizationManager.Get("AppErrorTitle"));
    }

    public void Save(Invoice inv)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Invoices, "invoice.update")) return;
        if (inv.Status == "Bezahlt" && string.IsNullOrEmpty(inv.PaidDate))
            inv.PaidDate = DateTime.Today.ToString("yyyy-MM-dd");
        if (inv.Id > 0) Database.Instance.UpdateInvoice(inv);
    }

    public void ApplyInvoiceChanges(Invoice target, Invoice source)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Invoices, "invoice.update")) return;
        if (target.Id != source.Id)
            throw new InvalidOperationException("Die bearbeitete Rechnung gehört nicht zum ausgewählten Datensatz.");

        // Persist the detached dialog copy first. If the database rejects the
        // update, the object currently displayed in the grid remains unchanged.
        Save(source);

        target.InvoiceNumber = source.InvoiceNumber;
        target.IssueDate = source.IssueDate;
        target.DueDate = source.DueDate;
        target.Customer = source.Customer;
        target.CustomerId = source.CustomerId;
        target.Amount = source.Amount;
        target.NetAmount = source.NetAmount;
        target.VatAmount = source.VatAmount;
        target.VatRate = source.VatRate;
        target.Description = source.Description;
        target.PaidDate = source.PaidDate;
        target.PaidAmount = source.PaidAmount;
        target.Status = source.Status;
        target.PdfPath = source.PdfPath;
        target.Content = source.Content?.DeepClone() ?? new DocumentContent();
    }

    public void RecalculateInvoiceAmounts(Invoice inv, string editedPropertyName)
    {
        switch (editedPropertyName)
        {
            case nameof(Invoice.Amount):
                amountCalculationBases[inv] = nameof(Invoice.Amount);
                CalculateFromGross(inv);
                break;
            case nameof(Invoice.NetAmount):
                amountCalculationBases[inv] = nameof(Invoice.NetAmount);
                CalculateFromNet(inv);
                break;
            case nameof(Invoice.VatAmount):
                amountCalculationBases[inv] = nameof(Invoice.VatAmount);
                CalculateFromVat(inv);
                break;
            case nameof(Invoice.VatRate):
                RecalculateAfterVatRateChange(inv);
                break;
        }
    }

    public void AddScannedInvoice(Invoice inv)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Invoices, "invoice.scan_import")) return;
        Database.Instance.AddInvoice(inv);
        Load();
    }

    private void RecalculateAfterVatRateChange(Invoice inv)
    {
        var calculationBase = amountCalculationBases.GetValueOrDefault(inv);
        if (calculationBase == nameof(Invoice.NetAmount) || (IsZero(inv.Amount) && !IsZero(inv.NetAmount)))
            CalculateFromNet(inv);
        else
            CalculateFromGross(inv);
    }

    private static void CalculateFromGross(Invoice inv)
    {
        inv.VatRate = NormalizeVatRate(inv.VatRate);
        if (IsZero(inv.Amount))
        {
            inv.NetAmount = 0;
            inv.VatAmount = 0;
            return;
        }

        if (IsZero(inv.VatRate))
        {
            inv.NetAmount = RoundCurrency(inv.Amount);
            inv.VatAmount = 0;
            return;
        }

        inv.NetAmount = RoundCurrency(inv.Amount / (1 + inv.VatRate / 100));
        inv.VatAmount = RoundCurrency(inv.Amount - inv.NetAmount);
    }

    private static void CalculateFromNet(Invoice inv)
    {
        inv.VatRate = NormalizeVatRate(inv.VatRate);
        inv.NetAmount = RoundCurrency(inv.NetAmount);
        inv.VatAmount = RoundCurrency(inv.NetAmount * inv.VatRate / 100);
        inv.Amount = RoundCurrency(inv.NetAmount + inv.VatAmount);
    }

    private static void CalculateFromVat(Invoice inv)
    {
        inv.VatAmount = RoundCurrency(inv.VatAmount);
        if (!IsZero(inv.NetAmount))
        {
            inv.Amount = RoundCurrency(inv.NetAmount + inv.VatAmount);
            inv.VatRate = RoundRate(inv.VatAmount / inv.NetAmount * 100);
        }
        else if (!IsZero(inv.Amount))
        {
            inv.NetAmount = RoundCurrency(inv.Amount - inv.VatAmount);
            inv.VatRate = !IsZero(inv.NetAmount)
                ? RoundRate(inv.VatAmount / inv.NetAmount * 100)
                : DefaultVatRate;
        }
    }

    private static double NormalizeVatRate(double vatRate) =>
        double.IsNaN(vatRate) || double.IsInfinity(vatRate) || vatRate < 0
            ? DefaultVatRate
            : RoundRate(vatRate);

    private static double RoundCurrency(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static double RoundRate(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsZero(double value) => Math.Abs(value) < 0.005;
}
