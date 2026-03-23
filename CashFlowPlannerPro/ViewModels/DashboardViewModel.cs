using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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

    [ObservableProperty] private ObservableCollection<MonthRow> monthRows = [];

    static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    static string Eur(double v) => v.ToString("N2", De) + " €";

    public void Load()
    {
        StartBalance = Database.Instance.GetSettingStartBalance();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var rows = Database.Instance.MonthlyCashflow(
            HorizonMonths, IncludeOffersOffen, IncludeOffersBeauftragt, IncludeInvoices, IncludeRecurring);
        var targets = Database.Instance.GetTargets();

        double cumulative = StartBalance;
        foreach (var r in rows) {
            cumulative += r.Net;
            r.Cumulative = cumulative;
            r.Target = targets.TryGetValue(r.Month, out var t) ? t : 0;
            r.Variance = r.Net - r.Target;
        }

        MonthRows = new ObservableCollection<MonthRow>(rows);

        // KPIs
        CurrentBalance = Eur(StartBalance + (rows.Count > 0 ? rows[0].Net : 0));
        bool anyNegative = rows.Any(r => r.Cumulative < 0);
        ForecastEnd = anyNegative ? "Geld reicht nicht!" : Eur(rows.Count > 0 ? rows[^1].Cumulative : StartBalance);
        MonthlyCashflow = rows.Count > 0 ? Eur(rows.Average(r => r.Net)) : Eur(0);
        ActiveOffers = Eur(Database.Instance.ActiveOffersSum());
        OpenInvoices = Eur(Database.Instance.OpenInvoicesSum());

        double avgExpenses = rows.Count > 0 ? rows.Average(r => Math.Abs(r.Expenses)) : 0;
        BurnRate = Eur(avgExpenses);

        double avgNet = rows.Count > 0 ? rows.Average(r => r.Net) : 0;
        if (avgNet < 0)
            Runway = Math.Ceiling(StartBalance / Math.Abs(avgNet)).ToString("N0", De) + " Monate";
        else
            Runway = "\u221e";
    }

    [RelayCommand]
    private void SaveBalance()
    {
        Database.Instance.SetSettingStartBalance(StartBalance);
    }
}
