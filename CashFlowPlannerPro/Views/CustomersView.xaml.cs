using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Linq;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class CustomersView : UserControl
{
    private readonly CustomersViewModel _vm;

    public CustomersView()
    {
        InitializeComponent();
        _vm = new CustomersViewModel();
        DataContext = _vm;
        _vm.Load();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                _vm.Load();
        };

        AddBtn.ToolTip = TooltipService.Get("Btn_AddCustomer");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteCustomer");
        var canEdit = App.CanEdit(PageKeys.Kunden);
        AddBtn.IsEnabled = canEdit;
        DeleteBtn.IsEnabled = canEdit;
        CustomersGrid.IsReadOnly = !canEdit;
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not Customer c)
            return;

        CommitEditingElement(e.EditingElement);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _vm.Save(c));
    }

    private static void CommitEditingElement(FrameworkElement editingElement)
    {
        switch (editingElement)
        {
            case TextBox textBox:
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            case ComboBox comboBox:
                comboBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
                comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                break;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Kunden, "customer.add")) return;
        var result = CustomerEditDialog.ShowNew();
        if (result != null)
        {
            _vm.Load();
            _vm.SelectedCustomer = _vm.Customers.FirstOrDefault(c => c.Id == result.Id);
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        CustomersGrid.Focus();
        CustomersGrid.SelectAll();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Kunden, "customer.delete")) return;
        var selectedCustomers = CustomersGrid.SelectedItems.Cast<Customer>().Distinct().ToList();
        if (selectedCustomers.Count == 0 && _vm.SelectedCustomer != null)
            selectedCustomers.Add(_vm.SelectedCustomer);

        if (selectedCustomers.Count == 0)
        {
            ModernMessageBox.Show("Bitte wähle zuerst mindestens einen Kunden aus.", "Adressbuch");
            return;
        }

        var message = selectedCustomers.Count == 1
            ? $"Soll \"{selectedCustomers[0].DisplayName}\" wirklich gelöscht werden?"
            : $"Sollen die {selectedCustomers.Count} ausgewählten Kunden wirklich gelöscht werden?";

        if (!ModernMessageBox.ShowConfirm(message, "Adressbuch"))
            return;

        _vm.DeleteCustomers(selectedCustomers);
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!PermissionGuard.DemandEdit(PageKeys.Kunden, "customer.update")) return;
        if (_vm.SelectedCustomer == null) return;
        var result = CustomerEditDialog.ShowEdit(_vm.SelectedCustomer);
        if (result != null)
        {
            _vm.Load();
            _vm.SelectedCustomer = _vm.Customers.FirstOrDefault(c => c.Id == result.Id);
        }
    }
}
