using System.Linq;
using System.Windows.Controls;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;

    public DashboardView()
    {
        InitializeComponent();
        _vm = new DashboardViewModel();
        DataContext = _vm;
        _vm.Load();
        UpdateChart();
    }

    private void UpdateChart()
    {
        if (_vm.MonthRows.Count == 0) return;
        var plt = Chart.Plot;
        plt.Clear();

        double[] netValues = _vm.MonthRows.Select(r => r.Net).ToArray();
        double[] cumValues = _vm.MonthRows.Select(r => r.Cumulative).ToArray();
        double[] positions = Enumerable.Range(0, netValues.Length).Select(i => (double)i).ToArray();

        var bars = plt.Add.Bars(positions, netValues);
        bars.Color = ScottPlot.Color.FromHex("#3B82F6");

        var line = plt.Add.Scatter(positions, cumValues);
        line.Color = ScottPlot.Color.FromHex("#EF4444");
        line.LineWidth = 2;

        string[] labels = _vm.MonthRows.Select(r => r.Month).ToArray();
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions.Select((p, i) => new ScottPlot.Tick(p, labels[i])).ToArray());
        plt.Axes.Bottom.TickLabelStyle.Rotation = 45;

        plt.Title("Cashflow Prognose");
        plt.YLabel("Euro (€)");
        plt.XLabel("Monat");

        Chart.Refresh();
    }
}
