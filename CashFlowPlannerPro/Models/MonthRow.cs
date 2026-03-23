namespace CashFlowPlannerPro.Models;

public class MonthRow
{
    public string Month { get; set; } = "";
    public double Net { get; set; }
    public double Income { get; set; }
    public double Expenses { get; set; }
    public double Cumulative { get; set; }
    public double Target { get; set; }
    public double Variance { get; set; }
    public double InvoiceAmount { get; set; }
}
