namespace CashFlowPlannerPro.Models;

public class Invoice
{
    public long Id { get; set; }
    public string IssueDate { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string Customer { get; set; } = "";
    public long CustomerId { get; set; }
    public double Amount { get; set; }
    public string Description { get; set; } = "";
    public string? PaidDate { get; set; }
    public double PaidAmount { get; set; }
    public string Status { get; set; } = "Offen";
    public string? PdfPath { get; set; }
    public string? CreatedAt { get; set; }
}
