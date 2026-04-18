using System.Collections.ObjectModel;
using System.Linq;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    private const string FixkostenMarker = "FIXKOSTEN:";
    private const string TaxMarker = "STEUER:";

    [ObservableProperty]
    private ObservableCollection<Transaction> transactions = [];

    [ObservableProperty]
    private Transaction? selectedTransaction;

    public List<string> IntervalOptions { get; } = ["einmalig", "t\u00e4glich", "w\u00f6chentlich", "monatlich", "viertelj\u00e4hrlich", "halbj\u00e4hrlich", "j\u00e4hrlich"];

    public void Load()
    {
        var all = Database.Instance.GetTransactions()
            .Where(t => !HasReservedMarker(t.Notes))
            .ToList();

        Transactions = new ObservableCollection<Transaction>(all);
        SelectedTransaction = Transactions.FirstOrDefault();
    }

    [RelayCommand]
    private void Add()
    {
        var t = new Transaction
        {
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Description = "",
            Amount = 0,
            Interval = "einmalig",
            Notes = ""
        };

        Database.Instance.AddTransaction(t);
        Transactions.Insert(0, t);
        SelectedTransaction = t;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTransaction == null) return;
        if (!ModernMessageBox.ShowConfirm("Transaktion wirklich loeschen?", "Loeschen")) return;

        var deleted = SelectedTransaction;
        Database.Instance.DeleteTransaction(deleted.Id);
        Transactions.Remove(deleted);
        SelectedTransaction = Transactions.FirstOrDefault();
    }

    public void Save(Transaction t)
    {
        t.Notes = NormalizeGeneralNotes(t.Notes);
        Database.Instance.UpdateTransaction(t);
    }

    private static bool HasReservedMarker(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return false;

        return notes.StartsWith(FixkostenMarker, StringComparison.OrdinalIgnoreCase)
            || notes.StartsWith(TaxMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGeneralNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";

        notes = ReplaceReservedMarker(notes, FixkostenMarker, "Fixkosten");
        notes = ReplaceReservedMarker(notes, TaxMarker, "Steuer");
        return notes;
    }

    private static string ReplaceReservedMarker(string notes, string marker, string label)
    {
        if (!notes.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            return notes;

        var remaining = notes[marker.Length..].TrimStart();
        return string.IsNullOrWhiteSpace(remaining) ? label : $"{label}: {remaining}";
    }
}
