namespace CashFlowPlannerPro.Models;

public class Offer
{
    public long Id { get; set; }
    public string OfferNumber { get; set; } = "";
    public string OfferDate { get; set; } = "";
    public string DateExpected { get; set; } = "";
    public string Customer { get; set; } = "";
    public long CustomerId { get; set; }
    public double Amount { get; set; }
    public double Probability { get; set; } = 50;
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Offen";
    public int PaymentDelay { get; set; } = 30;
    public string? PdfPath { get; set; }
    public string? CreatedAt { get; set; }
}
