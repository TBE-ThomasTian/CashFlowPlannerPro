using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
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

        AddBtn.ToolTip = TooltipService.Get("Btn_AddFixkosten");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteFixkosten");
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Transaction t)
            Dispatcher.BeginInvoke(() => _vm.Save(t));
    }
}
