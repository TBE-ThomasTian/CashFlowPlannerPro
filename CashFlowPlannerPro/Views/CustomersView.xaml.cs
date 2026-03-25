using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        AddBtn.ToolTip = TooltipService.Get("Btn_AddCustomer");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteCustomer");
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Customer c)
            Dispatcher.BeginInvoke(() => _vm.Save(c));
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var result = CustomerEditDialog.ShowNew();
        if (result != null)
        {
            _vm.Load();
            _vm.SelectedCustomer = _vm.Customers.FirstOrDefault(c => c.Id == result.Id);
        }
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedCustomer == null) return;
        var result = CustomerEditDialog.ShowEdit(_vm.SelectedCustomer);
        if (result != null)
        {
            _vm.Load();
            _vm.SelectedCustomer = _vm.Customers.FirstOrDefault(c => c.Id == result.Id);
        }
    }
}
