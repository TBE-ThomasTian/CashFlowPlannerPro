using System.Collections.ObjectModel;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class TaxesViewModel : ObservableObject
{
    private const string TaxMarker = "STEUER:";

    [ObservableProperty]
    private ObservableCollection<Transaction> taxes = [];

    [ObservableProperty]
    private Transaction? selectedTransaction;

    public List<string> IntervalOptions { get; } = ["Einmalig", "Monatlich", "Viertelj\u00e4hrlich", "J\u00e4hrlich"];
    public List<string> TaxTypes { get; } = ["Umsatzsteuer", "Gewerbesteuer", "Kapitalertragsteuer"];

    public void Load()
    {
        var items = Database.Instance.GetTransactions(TaxMarker);
        foreach (var t in items)
            t.CategoryName = ParseTaxType(t.Notes);

        Taxes = new ObservableCollection<Transaction>(items);
        SelectedTransaction = Taxes.FirstOrDefault();
    }

    private static string ParseTaxType(string? notes)
    {
        if (string.IsNullOrEmpty(notes) || !notes.StartsWith(TaxMarker, StringComparison.OrdinalIgnoreCase))
            return "Umsatzsteuer";

        var taxType = notes[TaxMarker.Length..].Trim();
        return string.IsNullOrWhiteSpace(taxType) ? "Umsatzsteuer" : taxType;
    }

    [RelayCommand]
    private void Add()
    {
        var t = new Transaction
        {
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Description = "Steuervorauszahlung",
            Amount = -1000,
            Interval = "Monatlich",
            Notes = TaxMarker + "Umsatzsteuer",
            CategoryName = "Umsatzsteuer"
        };

        Database.Instance.AddTransaction(t);
        Taxes.Insert(0, t);
        SelectedTransaction = t;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTransaction == null) return;
        if (!ModernMessageBox.ShowConfirm("Steuer-Eintrag wirklich loeschen?", "Loeschen")) return;

        var deleted = SelectedTransaction;
        Database.Instance.DeleteTransaction(deleted.Id);
        Taxes.Remove(deleted);
        SelectedTransaction = Taxes.FirstOrDefault();
    }

    public void Save(Transaction t)
    {
        var taxType = string.IsNullOrWhiteSpace(t.CategoryName) ? "Umsatzsteuer" : t.CategoryName.Trim();
        t.CategoryName = taxType;
        t.Notes = TaxMarker + taxType;
        Database.Instance.UpdateTransaction(t);
    }
}
