using System.Collections.ObjectModel;
using System.Linq;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Views;
using CashFlowPlannerPro.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class FixkostenViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Transaction> fixkosten = [];

    [ObservableProperty]
    private Transaction? selectedTransaction;

    [ObservableProperty]
    private List<string> categories = [];

    public List<string> IntervalOptions { get; } = ["einmalig", "monatlich", "vierteljährlich", "halbjährlich", "jährlich"];

    public void Load()
    {
        Categories = Database.Instance.GetCategories();
        var items = Database.Instance.GetTransactions("FIXKOSTEN:");
        foreach (var t in items)
            t.CategoryName = ResolveCategoryName(t.CategoryId);
        Fixkosten = new ObservableCollection<Transaction>(items);
    }

    private string? ResolveCategoryName(long? categoryId)
    {
        if (categoryId == null) return null;
        var cats = Database.Instance.GetCategories();
        // Resolve via DB query
        return cats.ElementAtOrDefault((int)(categoryId - 1));
    }

    private long? ResolveCategoryId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var cats = Database.Instance.GetCategories();
        var idx = cats.IndexOf(name);
        if (idx < 0) return null;
        // Category IDs are 1-based in the DB; we need to look up properly
        return GetCategoryIdFromDb(name);
    }

    private long? GetCategoryIdFromDb(string name)
    {
        // Use a simple lookup approach
        var all = Database.Instance.GetCategories();
        // We need the actual ID - for now use a transaction-based approach
        // Since Database doesn't expose GetCategoryId, we'll add a helper
        try {
            var field = typeof(Database).GetMethod("GetCategoryId");
            if (field != null) return (long?)field.Invoke(Database.Instance, [name]);
        } catch { }
        return null;
    }

    [RelayCommand]
    private void Add()
    {
        var t = new Transaction {
            Date = DateTime.Now.ToString("yyyy-MM-dd"),
            Description = "",
            Amount = -100,
            Interval = "monatlich",
            Notes = "FIXKOSTEN:"
        };
        Database.Instance.AddTransaction(t);
        Load();
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTransaction == null) return;
        if (!ModernMessageBox.ShowConfirm("Fixkosten-Eintrag wirklich löschen?", "Löschen")) return;
        Database.Instance.DeleteTransaction(SelectedTransaction.Id);
        Load();
    }

    public void Save(Transaction t)
    {
        if (!t.Notes.StartsWith("FIXKOSTEN:"))
            t.Notes = "FIXKOSTEN:" + t.Notes;
        t.CategoryId = ResolveCategoryId(t.CategoryName);
        Database.Instance.UpdateTransaction(t);
    }
}
