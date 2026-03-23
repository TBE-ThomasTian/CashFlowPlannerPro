using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class InvoicesView : UserControl
{
    private readonly InvoicesViewModel _vm;

    public InvoicesView()
    {
        InitializeComponent();
        _vm = new InvoicesViewModel();
        DataContext = _vm;
        _vm.Load();
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Invoice inv)
            Dispatcher.BeginInvoke(() => _vm.Save(inv));
    }
}
