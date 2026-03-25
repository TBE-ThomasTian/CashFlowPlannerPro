using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class TaxesView : UserControl
{
    private readonly TaxesViewModel _vm;

    public TaxesView()
    {
        InitializeComponent();
        _vm = new TaxesViewModel();
        DataContext = _vm;
        _vm.Load();

        AddBtn.ToolTip = TooltipService.Get("Btn_AddTax");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteTax");
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Transaction t)
            Dispatcher.BeginInvoke(() => _vm.Save(t));
    }
}
