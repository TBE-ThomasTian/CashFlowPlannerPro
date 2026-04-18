using System.Collections.ObjectModel;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Views;
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

    public List<string> IntervalOptions { get; } = ["einmalig", "monatlich", "viertelj\u00e4hrlich", "halbj\u00e4hrlich", "j\u00e4hrlich"];

    public void Load()
    {
        Categories = Database.Instance.GetCategories();
        var items = Database.Instance.GetTransactions("FIXKOSTEN:");
        foreach (var t in items)
            t.CategoryName = Database.Instance.GetCategoryName(t.CategoryId);

        Fixkosten = new ObservableCollection<Transaction>(items);
        SelectedTransaction = Fixkosten.FirstOrDefault();
    }

    [RelayCommand]
    private void Add()
    {
        AddFixkosten(CreateDefaultFixkosten());
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedTransaction == null) return;
        if (!ModernMessageBox.ShowConfirm("Fixkosten-Eintrag wirklich loeschen?", "Loeschen")) return;
        Database.Instance.DeleteTransaction(SelectedTransaction.Id);
        Fixkosten.Remove(SelectedTransaction);
        SelectedTransaction = Fixkosten.FirstOrDefault();
    }

    public Transaction CreateDefaultFixkosten() =>
        new()
        {
            Date = DateTime.Today.ToString("yyyy-MM-dd"),
            Description = "",
            Amount = -100,
            Interval = "monatlich",
            Notes = "FIXKOSTEN:"
        };

    public void AddFixkosten(Transaction t)
    {
        Save(t);
        Fixkosten.Insert(0, t);
        SelectedTransaction = t;
    }

    public void ApplyFixkostenChanges(Transaction target, Transaction source)
    {
        var index = Fixkosten.IndexOf(target);
        if (index < 0)
            return;

        var updated = new Transaction
        {
            Id = target.Id,
            Date = source.Date,
            Description = source.Description,
            Amount = source.Amount,
            CategoryId = target.CategoryId,
            PersonId = target.PersonId,
            Interval = source.Interval,
            Notes = source.Notes,
            CreatedAt = target.CreatedAt,
            UpdatedAt = target.UpdatedAt,
            CategoryName = source.CategoryName
        };

        Save(updated);
        Fixkosten[index] = updated;
        SelectedTransaction = updated;
    }

    public void Save(Transaction t)
    {
        NormalizeFixkosten(t);
        t.CategoryId = ResolveCategoryId(t.CategoryName);

        if (t.Id > 0)
            Database.Instance.UpdateTransaction(t);
        else
            Database.Instance.AddTransaction(t);
    }

    private long? ResolveCategoryId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Database.Instance.GetCategoryId(name.Trim());
    }

    private static void NormalizeFixkosten(Transaction t)
    {
        t.Notes = EnsureFixkostenMarker(t.Notes);
        if (t.Amount > 0)
            t.Amount = -t.Amount;
    }

    private static string EnsureFixkostenMarker(string? notes)
    {
        notes ??= "";
        return notes.StartsWith("FIXKOSTEN:", StringComparison.OrdinalIgnoreCase)
            ? notes
            : "FIXKOSTEN:" + notes;
    }
}
