using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Transaction> transactions = [];

    [ObservableProperty]
    private Transaction? selectedTransaction;

    public List<string> IntervalOptions { get; } = ["einmalig", "täglich", "wöchentlich", "monatlich", "vierteljährlich", "halbjährlich", "jährlich"];

    public void Load()
    {
        var all = Database.Instance.GetTransactions()
            .Where(t => !t.Notes.StartsWith("FIXKOSTEN:") && !t.Notes.StartsWith("STEUER:"))
            .ToList();
        Transactions = new ObservableCollection<Transaction>(all);
    }

    [RelayCommand]
    private void Add()
    {
        var t = new Transaction {
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            Description = "",
            Amount = 0,
            Interval = "einmalig",
            Notes = ""
        };
        Database.Instance.AddTransaction(t);
        Load();
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTransaction == null) return;
        if (MessageBox.Show("Transaktion wirklich löschen?", "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Database.Instance.DeleteTransaction(SelectedTransaction.Id);
        Load();
    }

    public void Save(Transaction t)
    {
        Database.Instance.UpdateTransaction(t);
    }
}
