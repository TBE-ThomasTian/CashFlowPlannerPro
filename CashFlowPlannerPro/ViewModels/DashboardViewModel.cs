using System.Collections.ObjectModel;
using System.Globalization;
using CashFlowPlannerPro.Data;
using CashFlowPlannerPro.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashFlowPlannerPro.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private double startBalance;
    [ObservableProperty] private int horizonMonths = 12;
    [ObservableProperty] private bool includeInvoices = true;
    [ObservableProperty] private bool includeOffersOffen = true;
    [ObservableProperty] private bool includeOffersBeauftragt = true;
    [ObservableProperty] private bool includeRecurring = true;

    [ObservableProperty] private string currentBalance = "";
    [ObservableProperty] private string forecastEnd = "";
    [ObservableProperty] private string monthlyCashflow = "";
    [ObservableProperty] private string activeOffers = "";
    [ObservableProperty] private string openInvoices = "";
    [ObservableProperty] private string burnRate = "";
    [ObservableProperty] private string runway = "";
    [ObservableProperty] private string openTodos = "";
    [ObservableProperty] private string overdueTodos = "";
    [ObservableProperty] private string teamUtilization = "";
    [ObservableProperty] private string hoursThisMonth = "";
    [ObservableProperty] private string runningTimers = "";
    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private ObservableCollection<MonthRow> monthRows = [];

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string Eur(double value) => value.ToString("N2", De) + " \u20ac";

    partial void OnStartBalanceChanged(double value)
    {
        CurrentBalance = Eur(value);

        if (MonthRows.Count > 0)
            ApplyForecastRows(MonthRows);
        else
            ForecastEnd = Eur(value);
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            StartBalance = await Task.Run(() => Database.Instance.GetSettingStartBalance());
            await Refresh();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        IsBusy = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var rows = Database.Instance.MonthlyCashflow(
                    HorizonMonths, IncludeOffersOffen, IncludeOffersBeauftragt, IncludeInvoices, IncludeRecurring);
                var targets = Database.Instance.GetTargets();
                string activeOffers = Eur(Database.Instance.ActiveOffersSum());
                string openInvoices = Eur(Database.Instance.OpenInvoicesSum());
                string hoursThisMonth = Database.Instance.GetHoursBookedThisMonth().ToString("N1", De) + " h";
                string runningTimers = Database.Instance.CountRunningTimeEntries().ToString("N0", De);
                var todos = Database.Instance.GetAllTodos();
                var resources = Database.Instance.GetResources();

                return new DashboardSnapshot
                {
                    Rows = rows,
                    Targets = targets,
                    ActiveOffers = activeOffers,
                    OpenInvoices = openInvoices,
                    HoursThisMonth = hoursThisMonth,
                    RunningTimers = runningTimers,
                    Todos = todos,
                    Resources = resources
                };
            });

            ApplyForecastRows(snapshot.Rows, snapshot.Targets);

            MonthlyCashflow = snapshot.Rows.Count > 0 ? Eur(snapshot.Rows.Average(r => r.Net)) : Eur(0);
            ActiveOffers = snapshot.ActiveOffers;
            OpenInvoices = snapshot.OpenInvoices;
            HoursThisMonth = snapshot.HoursThisMonth;
            RunningTimers = snapshot.RunningTimers;

            double avgExpenses = snapshot.Rows.Count > 0 ? snapshot.Rows.Average(r => Math.Abs(r.Expenses)) : 0;
            BurnRate = Eur(avgExpenses);

            OpenTodos = snapshot.Todos.Count(t => !string.Equals(t.Status, "Erledigt", StringComparison.OrdinalIgnoreCase)).ToString("N0", De);
            OverdueTodos = snapshot.Todos.Count(t =>
                !string.Equals(t.Status, "Erledigt", StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParse(t.DueDate, out var due)
                && due.Date < DateTime.Today).ToString("N0", De);

            TeamUtilization = snapshot.Resources.Count == 0
                ? "0 %"
                : Math.Round(snapshot.Resources.Average(r => r.Availability) * 100).ToString("N0", De) + " %";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyForecastRows(IEnumerable<MonthRow> rows, Dictionary<string, double>? targets = null)
    {
        double cumulative = StartBalance;
        var updatedRows = rows.Select(r =>
        {
            var target = targets != null && targets.TryGetValue(r.Month, out var targetValue)
                ? targetValue
                : r.Target;

            cumulative += r.Net;
            return new MonthRow
            {
                Month = r.Month,
                Income = r.Income,
                Expenses = r.Expenses,
                Net = r.Net,
                Cumulative = cumulative,
                Target = target,
                Variance = r.Net - target,
                InvoiceAmount = r.InvoiceAmount
            };
        }).ToList();

        MonthRows = new ObservableCollection<MonthRow>(updatedRows);
        CurrentBalance = Eur(StartBalance);

        bool anyNegative = updatedRows.Any(r => r.Cumulative < 0);
        ForecastEnd = anyNegative ? "Geld reicht nicht!" : Eur(updatedRows.Count > 0 ? updatedRows[^1].Cumulative : StartBalance);

        double avgNet = updatedRows.Count > 0 ? updatedRows.Average(r => r.Net) : 0;
        Runway = avgNet < 0
            ? Math.Ceiling(StartBalance / Math.Abs(avgNet)).ToString("N0", De) + " Monate"
            : "\u221e";
    }

    [RelayCommand]
    private void SaveBalance()
    {
        Database.Instance.SetSettingStartBalance(StartBalance);
    }

    private sealed class DashboardSnapshot
    {
        public List<MonthRow> Rows { get; init; } = [];
        public Dictionary<string, double> Targets { get; init; } = [];
        public string ActiveOffers { get; init; } = "";
        public string OpenInvoices { get; init; } = "";
        public string HoursThisMonth { get; init; } = "";
        public string RunningTimers { get; init; } = "";
        public List<UserTodo> Todos { get; init; } = [];
        public List<Resource> Resources { get; init; } = [];
    }
}
