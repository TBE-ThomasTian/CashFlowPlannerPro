using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class CustomersViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Customer> customers = new();
    [ObservableProperty] private Customer? selectedCustomer;
    [ObservableProperty] private string searchText = "";

    private List<Customer> _allCustomers = [];

    public void Load()
    {
        _allCustomers = Database.Instance.GetCustomers();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allCustomers
            : _allCustomers.Where(c =>
                c.CustomerNumber.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Company.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.ContactName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Email.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.City.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                c.Phone.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        Customers = new ObservableCollection<Customer>(filtered);
    }

    [RelayCommand]
    private void Add()
    {
        var c = new Customer { Status = "Aktiv", Country = "Deutschland" };
        Database.Instance.AddCustomer(c);
        _allCustomers.Add(c);
        ApplyFilter();
        SelectedCustomer = c;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedCustomer == null) return;
        Database.Instance.DeleteCustomer(SelectedCustomer.Id);
        _allCustomers.Remove(SelectedCustomer);
        Customers.Remove(SelectedCustomer);
    }

    public void DeleteCustomers(IEnumerable<Customer> customersToDelete)
    {
        var ids = customersToDelete
            .Where(c => c.Id > 0)
            .Select(c => c.Id)
            .Distinct()
            .ToHashSet();

        if (ids.Count == 0)
            return;

        foreach (var id in ids)
            Database.Instance.DeleteCustomer(id);

        _allCustomers = _allCustomers.Where(c => !ids.Contains(c.Id)).ToList();
        ApplyFilter();
        SelectedCustomer = Customers.FirstOrDefault();
    }

    public void Save(Customer c)
    {
        if (c.Id > 0) Database.Instance.UpdateCustomer(c);
    }
}
