using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;
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

        AddBtn.ToolTip = TooltipService.Get("Btn_AddTarget");
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteTarget");
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not Target t)
            return;

        CommitEditingElement(e.EditingElement);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _vm.Save(t));
    }

    private static void CommitEditingElement(FrameworkElement editingElement)
    {
        if (editingElement is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}
