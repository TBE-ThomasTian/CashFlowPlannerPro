using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class FixkostenView : UserControl
{
    private readonly FixkostenViewModel _vm;

    public FixkostenView()
    {
        InitializeComponent();
        _vm = new FixkostenViewModel();
        DataContext = _vm;
        _vm.Load();
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Transaction t)
            Dispatcher.BeginInvoke(() => _vm.Save(t));
    }
}
