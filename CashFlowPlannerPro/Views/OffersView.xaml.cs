using System.Windows.Controls;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class OffersView : UserControl
{
    private readonly OffersViewModel _vm;

    public OffersView()
    {
        InitializeComponent();
        _vm = new OffersViewModel();
        DataContext = _vm;
        _vm.Load();
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.Item is Offer o)
            Dispatcher.BeginInvoke(() => _vm.Save(o));
    }
}
