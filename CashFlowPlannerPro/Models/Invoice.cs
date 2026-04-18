using CommunityToolkit.Mvvm.ComponentModel;

namespace CashFlowPlannerPro.Models;

public class Invoice : ObservableObject
{
    private long id;
    private string issueDate = "";
    private string dueDate = "";
    private string customer = "";
    private long customerId;
    private double amount;
    private double netAmount;
    private double vatAmount;
    private double vatRate = 19;
    private string description = "";
    private string? paidDate;
    private double paidAmount;
    private string status = "Offen";
    private string? pdfPath;
    private string? createdAt;

    public long Id { get => id; set => SetProperty(ref id, value); }
    public string IssueDate { get => issueDate; set => SetProperty(ref issueDate, value); }
    public string DueDate { get => dueDate; set => SetProperty(ref dueDate, value); }
    public string Customer { get => customer; set => SetProperty(ref customer, value); }
    public long CustomerId { get => customerId; set => SetProperty(ref customerId, value); }
    public double Amount { get => amount; set => SetProperty(ref amount, value); }
    public double NetAmount { get => netAmount; set => SetProperty(ref netAmount, value); }
    public double VatAmount { get => vatAmount; set => SetProperty(ref vatAmount, value); }
    public double VatRate { get => vatRate; set => SetProperty(ref vatRate, value); }
    public string Description { get => description; set => SetProperty(ref description, value); }
    public string? PaidDate { get => paidDate; set => SetProperty(ref paidDate, value); }
    public double PaidAmount { get => paidAmount; set => SetProperty(ref paidAmount, value); }
    public string Status { get => status; set => SetProperty(ref status, value); }
    public string? PdfPath { get => pdfPath; set => SetProperty(ref pdfPath, value); }
    public string? CreatedAt { get => createdAt; set => SetProperty(ref createdAt, value); }
}
