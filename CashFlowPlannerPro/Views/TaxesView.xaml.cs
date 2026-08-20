using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
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
        var canEdit = App.CanEdit(PageKeys.Taxes);
        AddBtn.IsEnabled = canEdit;
        DeleteBtn.IsEnabled = canEdit;
        TaxesGrid.IsReadOnly = !canEdit;
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not Transaction t)
            return;

        CommitEditingElement(e.EditingElement);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _vm.Save(t));
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
                break;
        }
    }
}
