using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class TargetsView : UserControl
{
    private readonly TargetsViewModel _vm;

    public TargetsView()
    {
        InitializeComponent();
        _vm = new TargetsViewModel();
        DataContext = _vm;
        _vm.Load();
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Target t)
            Dispatcher.BeginInvoke(() => _vm.Save(t));
    }
}
