namespace CashFlowPlannerPro.Models;

public class Transaction
{
    public long Id { get; set; }
    public string Date { get; set; } = "";
    public string Description { get; set; } = "";
    public double Amount { get; set; }
    public long? CategoryId { get; set; }
    public long? PersonId { get; set; }
    public string Interval { get; set; } = "einmalig";
    public string Notes { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public string? CategoryName { get; set; }
}
