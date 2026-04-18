using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        EditBtn.ToolTip = "Ausgewaehlte Fixkosten bearbeiten";
        DeleteBtn.ToolTip = TooltipService.Get("Btn_DeleteFixkosten");
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var result = FixkostenEditDialog.ShowNew(_vm.CreateDefaultFixkosten(), _vm.Categories, _vm.IntervalOptions);
        if (result == null)
            return;

        _vm.AddFixkosten(result);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedFixkosten();
    }

    private void FixkostenGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EditSelectedFixkosten();
    }

    private void EditSelectedFixkosten()
    {
        if (_vm.SelectedTransaction == null)
        {
            ModernMessageBox.Show("Bitte waehle zuerst einen Fixkosten-Eintrag aus.", "Fixkosten");
            return;
        }

        var result = FixkostenEditDialog.ShowEdit(_vm.SelectedTransaction, _vm.Categories, _vm.IntervalOptions);
        if (result == null)
            return;

        _vm.ApplyFixkostenChanges(_vm.SelectedTransaction, result);
    }
}
