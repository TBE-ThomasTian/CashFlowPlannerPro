using System.Linq;
using System.Windows.Controls;
using CashFlowPlannerPro.Services;
using CashFlowPlannerPro.ViewModels;
using Microsoft.Win32;

namespace CashFlowPlannerPro.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;
    private bool _isSubscribed;
    private Border[]? _miniBars;

    public DashboardView()
    {
        InitializeComponent();
        _vm = new DashboardViewModel();
        DataContext = _vm;
        _miniBars = [MiniBar0, MiniBar1, MiniBar2, MiniBar3, MiniBar4, MiniBar5, MiniBar6];

        RefreshButton.ToolTip = TooltipService.Get("Btn_Refresh");
        SaveBalanceButton.ToolTip = TooltipService.Get("Btn_SaveBalance");
        PreviewPdfButton.ToolTip = TooltipService.Get("Btn_PreviewDashboardPdf");
        ExportPdfButton.ToolTip = TooltipService.Get("Btn_ExportDashboardPdf");

        ApplyLocalization();
        Loaded += (_, _) =>
        {
            if (_isSubscribed) return;
            LocalizationManager.LanguageChanged += OnLanguageChanged;
            _isSubscribed = true;
        };
        _vm.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(DashboardViewModel.MonthRows))
            {
                UpdateChart();
                UpdateMiniBars();
            }
        };
        _vm.Load();
        UpdateChart();
        UpdateMiniBars();
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
        UpdateMiniBars();
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
        PreviewPdfButton.Content = LocalizationManager.Get("DashboardPreviewPdf");
        ExportPdfButton.Content = LocalizationManager.Get("DashboardExportPdf");

        CurrentBalanceLabel.Text = LocalizationManager.Get("DashboardCurrentBalance");
        ForecastEndLabel.Text = LocalizationManager.Get("DashboardForecastEnd");
        MonthlyCashflowLabel.Text = LocalizationManager.Get("DashboardMonthlyCashflow");
        ActiveOffersLabel.Text = LocalizationManager.Get("DashboardActiveOffers");
        OpenInvoicesLabel.Text = LocalizationManager.Get("DashboardOpenInvoices");
        OpenTodosLabel.Text = LocalizationManager.Get("DashboardOpenTodos");
        OverdueTodosLabel.Text = LocalizationManager.Get("DashboardOverdueTodos");
        TeamUtilizationLabel.Text = LocalizationManager.Get("DashboardTeamUtilization");
        HoursThisMonthLabel.Text = LocalizationManager.Get("DashboardHoursThisMonth");
        RunningTimersLabel.Text = LocalizationManager.Get("DashboardRunningTimers");
        LiveFinanceTitleText.Text = LocalizationManager.Get("DashboardLiveFinance");
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

    private void UpdateMiniBars()
    {
        if (_miniBars == null || _miniBars.Length == 0)
            return;

        const double minHeight = 28;
        const double maxHeight = 98;

        var values = _vm.MonthRows
            .Take(_miniBars.Length)
            .Select(r => Math.Abs(r.Net))
            .ToArray();

        if (values.Length == 0)
        {
            foreach (var bar in _miniBars)
            {
                bar.Height = minHeight;
                bar.Opacity = 0.35;
                bar.ToolTip = null;
            }
            return;
        }

        double maxValue = values.Max();
        if (maxValue <= 0)
        {
            foreach (var bar in _miniBars)
            {
                bar.Height = minHeight;
                bar.Opacity = 1.0;
            }
            return;
        }

        for (int i = 0; i < _miniBars.Length; i++)
        {
            bool hasValue = i < values.Length;
            double value = hasValue ? values[i] : 0;
            double normalized = maxValue > 0 ? value / maxValue : 0;

            _miniBars[i].Height = minHeight + ((maxHeight - minHeight) * normalized);
            _miniBars[i].Opacity = hasValue ? 1.0 : 0.35;
            _miniBars[i].ToolTip = hasValue
                ? $"{_vm.MonthRows[i].Month}: {_vm.MonthRows[i].Net:N2} €"
                : null;
        }
    }

    private static string ToAxisLabel(string monthLabel)
    {
        var parts = monthLabel.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
            return $"{parts[0]}\n20{parts[1]}";
        return monthLabel;
    }

    private void ExportPdf_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.Get("DashboardPdfDialogTitle"),
            Filter = LocalizationManager.Get("DashboardPdfDialogFilter"),
            DefaultExt = ".pdf",
            FileName = $"CashFlowPlannerPro-Monatsreport-{DateTime.Now:yyyyMMdd}.pdf"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            DashboardPdfReportService.ExportMonthlyReport(dialog.FileName, _vm);
            ModernMessageBox.Show(
                string.Format(LocalizationManager.Get("DashboardPdfSuccess"), dialog.FileName),
                LocalizationManager.Get("DashboardPdfTitle"));
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("DashboardPdfFailed"), ex.Message),
                LocalizationManager.Get("DashboardPdfTitle"));
        }
    }

    private void PreviewPdf_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            var window = new ReportPreviewWindow(
                LocalizationManager.Get("DashboardPreviewTitle"),
                LocalizationManager.Get("DashboardPreviewSubtitle"),
                _vm);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            ModernMessageBox.ShowError(
                string.Format(LocalizationManager.Get("DashboardPdfFailed"), ex.Message),
                LocalizationManager.Get("DashboardPdfTitle"));
        }
    }
}
