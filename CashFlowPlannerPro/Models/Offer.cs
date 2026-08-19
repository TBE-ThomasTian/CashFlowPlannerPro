namespace CashFlowPlannerPro.Models;

public class Offer
{
    public long Id { get; set; }
    public string OfferNumber { get; set; } = "";
    public string OfferDate { get; set; } = "";
    public string DateExpected { get; set; } = "";
    public string Customer { get; set; } = "";
    public long CustomerId { get; set; }
    public double AmountBeforeDiscount { get; set; }
    public double DiscountPercent { get; set; }
    public double Amount { get; set; }
    public double DiscountAmount => Math.Round(
        Math.Max(0, AmountBeforeDiscount - Amount),
        2,
        MidpointRounding.AwayFromZero);
    public double Probability { get; set; } = 50;
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Offen";
    public int PaymentDelay { get; set; } = 30;
    public string? PdfPath { get; set; }
    public string? CreatedAt { get; set; }
    public long? ProjectId { get; set; }
    public string ProjectNumber { get; set; } = "";
    public DocumentContent Content { get; set; } = new();
}
