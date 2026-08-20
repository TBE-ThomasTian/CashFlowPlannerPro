using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
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
        var canEdit = App.CanEdit(PageKeys.Transactions);
        AddBtn.IsEnabled = canEdit;
        DeleteBtn.IsEnabled = canEdit;
        TransactionsGrid.IsReadOnly = !canEdit;
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
