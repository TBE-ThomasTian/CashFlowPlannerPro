using System.Linq;
using System.Windows.Controls;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;

namespace CashFlowPlannerPro.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;
    private bool _isSubscribed;

    public DashboardView()
    {
        InitializeComponent();
        _vm = new DashboardViewModel();
        DataContext = _vm;

        RefreshButton.ToolTip = TooltipService.Get("Btn_Refresh");
        SaveBalanceButton.ToolTip = TooltipService.Get("Btn_SaveBalance");

        ApplyLocalization();
        Loaded += (_, _) =>
        {
            if (_isSubscribed) return;
            LocalizationManager.LanguageChanged += OnLanguageChanged;
            _isSubscribed = true;
        };
        _vm.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(DashboardViewModel.MonthRows))
                UpdateChart();
        };
        _vm.Load();
        UpdateChart();
        Unloaded += (_, _) =>
        {
            if (!_isSubscribed) return;
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            _isSubscribed = false;
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        UpdateChart();
    }

    private void ApplyLocalization()
    {
        BalanceLabel.Content = LocalizationManager.Get("DashboardBalance");
        ForecastMonthsLabel.Content = LocalizationManager.Get("DashboardForecastMonths");
        InvoicesCheckBox.Content = LocalizationManager.Get("DashboardInvoices");
        OpenOffersCheckBox.Content = LocalizationManager.Get("DashboardOpenOffers");
        AssignedOffersCheckBox.Content = LocalizationManager.Get("DashboardAssignedOffers");
        RecurringCheckBox.Content = LocalizationManager.Get("DashboardRecurring");
        RefreshButton.Content = LocalizationManager.Get("DashboardRefresh");
        SaveBalanceButton.Content = LocalizationManager.Get("DashboardSaveBalance");

        CurrentBalanceLabel.Text = LocalizationManager.Get("DashboardCurrentBalance");
        ForecastEndLabel.Text = LocalizationManager.Get("DashboardForecastEnd");
        MonthlyCashflowLabel.Text = LocalizationManager.Get("DashboardMonthlyCashflow");
        ActiveOffersLabel.Text = LocalizationManager.Get("DashboardActiveOffers");
        OpenInvoicesLabel.Text = LocalizationManager.Get("DashboardOpenInvoices");
        BurnRateLabel.Text = LocalizationManager.Get("DashboardBurnRate");
        RunwayLabel.Text = LocalizationManager.Get("DashboardRunway");
        ChartTitleText.Text = LocalizationManager.Get("DashboardChartTitle");
        ChartSubtitleText.Text = LocalizationManager.Get("DashboardChartSubtitle");

        MonthColumn.Header = LocalizationManager.Get("DashboardMonth");
        IncomeColumn.Header = LocalizationManager.Get("DashboardIncome");
        ExpensesColumn.Header = LocalizationManager.Get("DashboardExpenses");
        NetColumn.Header = LocalizationManager.Get("DashboardNet");
        CumulativeColumn.Header = LocalizationManager.Get("DashboardCumulative");
        TargetColumn.Header = LocalizationManager.Get("DashboardTarget");
        VarianceColumn.Header = LocalizationManager.Get("DashboardVariance");
        InvoiceAmountColumn.Header = LocalizationManager.Get("DashboardInvoiceAmount");
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
        bars.Color = ScottPlot.Color.FromHex("#812B8C");

        var line = plt.Add.Scatter(positions, cumValues);
        line.Color = ScottPlot.Color.FromHex("#D9731A");
        line.LineWidth = 3;
        line.MarkerSize = 6;

        string[] labels = _vm.MonthRows.Select(r => r.Month).ToArray();
        plt.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions.Select((p, i) => new ScottPlot.Tick(p, ToAxisLabel(labels[i]))).ToArray());
        plt.Axes.Bottom.TickLabelStyle.Rotation = 0;

        plt.YLabel(LocalizationManager.Get("DashboardYAxisEuro"));

        Chart.Refresh();
    }

    private static string ToAxisLabel(string monthLabel)
    {
        var parts = monthLabel.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
            return $"{parts[0]}\n20{parts[1]}";
        return monthLabel;
    }
}
