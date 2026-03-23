using System.Collections.ObjectModel;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Views;
using CashFlowPlannerPro.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class TaxesViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Transaction> taxes = [];

    [ObservableProperty]
    private Transaction? selectedTransaction;

    public List<string> IntervalOptions { get; } = ["Einmalig", "Monatlich", "Vierteljährlich", "Jährlich"];
    public List<string> TaxTypes { get; } = ["Umsatzsteuer", "Gewerbesteuer", "Kapitalertragsteuer"];

    public void Load()
    {
        var items = Database.Instance.GetTransactions("STEUER:");
        foreach (var t in items)
            t.CategoryName = ParseTaxType(t.Notes);
        Taxes = new ObservableCollection<Transaction>(items);
    }

    private static string ParseTaxType(string notes)
    {
        if (string.IsNullOrEmpty(notes) || !notes.StartsWith("STEUER:")) return "";
        return notes["STEUER:".Length..];
    }

    [RelayCommand]
    private void Add()
    {
        var t = new Transaction {
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            Description = "Steuervorauszahlung",
            Amount = -1000,
            Interval = "Monatlich",
            Notes = "STEUER:Umsatzsteuer",
            CategoryName = "Umsatzsteuer"
        };
        Database.Instance.AddTransaction(t);
        Load();
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTransaction == null) return;
        if (!ModernMessageBox.ShowConfirm("Steuer-Eintrag wirklich löschen?", "Löschen")) return;
        Database.Instance.DeleteTransaction(SelectedTransaction.Id);
        Load();
    }

    public void Save(Transaction t)
    {
        var taxType = t.CategoryName ?? "Umsatzsteuer";
        t.Notes = "STEUER:" + taxType;
        Database.Instance.UpdateTransaction(t);
    }
}
