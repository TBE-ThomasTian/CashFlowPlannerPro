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
    [ObservableProperty] private Invoice? selectedInvoice;

    public void Load()
    {
        Invoices = new ObservableCollection<Invoice>(Database.Instance.GetInvoices());
    }

    [RelayCommand]
    private void Add()
    {
        var inv = new Invoice {
            IssueDate = DateTime.Today.ToString("yyyy-MM-dd"),
            DueDate = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),
            Status = "Offen",
            Amount = 0
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
}
