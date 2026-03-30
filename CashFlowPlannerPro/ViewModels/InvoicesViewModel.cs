using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CashFlowPlannerPro.ViewModels;

public partial class InvoicesViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Invoice> invoices = new();
    [ObservableProperty] private ObservableCollection<string> customerNames = new();
    [ObservableProperty] private Invoice? selectedInvoice;

    public void Load()
    {
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
        var inv = new Invoice {
            IssueDate = DateTime.Today.ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),
            Status = "Offen",
            Amount = 0,
            Customer = CustomerNames.FirstOrDefault() ?? ""
        };
        Database.Instance.AddInvoice(inv);
        Invoices.Add(inv);
        SelectedInvoice = inv;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedInvoice == null) return;
        Database.Instance.DeleteInvoice(SelectedInvoice.Id);
        Invoices.Remove(SelectedInvoice);
    }

    public void DeleteInvoices(IEnumerable<Invoice> invoicesToDelete)
    {
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
        if (SelectedInvoice?.PdfPath == null || !File.Exists(SelectedInvoice.PdfPath)) return;
        Process.Start(new ProcessStartInfo(SelectedInvoice.PdfPath) { UseShellExecute = true });
    }

    public void Save(Invoice inv)
    {
        if (inv.Status == "Bezahlt" && string.IsNullOrEmpty(inv.PaidDate))
            inv.PaidDate = DateTime.Today.ToString("yyyy-MM-dd");
        if (inv.Id > 0) Database.Instance.UpdateInvoice(inv);
    }

    public void AddScannedInvoice(Invoice inv)
    {
        Database.Instance.AddInvoice(inv);
        Load();
    }
}
