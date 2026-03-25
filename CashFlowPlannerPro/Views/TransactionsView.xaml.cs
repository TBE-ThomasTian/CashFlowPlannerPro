using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class TransactionsView : UserControl
{
    private readonly TransactionsViewModel _vm;

    public TransactionsView()
    {
        InitializeComponent();
        _vm = new TransactionsViewModel();
        DataContext = _vm;
        _vm.Load();

        AddBtn.ToolTip = TooltipService.Get("Btn_AddTransaction");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteTransaction");
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Transaction t)
            Dispatcher.BeginInvoke(() => _vm.Save(t));
    }
}
