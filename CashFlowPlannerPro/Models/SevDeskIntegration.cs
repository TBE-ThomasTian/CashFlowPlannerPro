using System.Globalization;

namespace CashFlowPlannerPro.Models;

public sealed class SevDeskSecureData
{
    public string ApiToken { get; set; } = "";
}

public sealed class SevDeskImportPreview
{
    public List<SevDeskContactPreview> Contacts { get; set; } = [];
    public List<SevDeskInvoicePreview> Invoices { get; set; } = [];
    public List<SevDeskOfferPreview> Offers { get; set; } = [];
}

public sealed class SevDeskContactPreview
{
    public bool IsSelected { get; set; } = true;
    public bool ExistsLocally { get; set; }
    public string ImportState { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Company { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Street { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "Deutschland";
    public string TaxId { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Company) ? ContactName : Company;

    public Customer ToCustomer()
    {
        return new Customer
        {
            Company = Company,
            ContactName = ContactName,
            Email = Email,
            Phone = Phone,
            Street = Street,
            ZipCode = ZipCode,
            City = City,
            Country = string.IsNullOrWhiteSpace(Country) ? "Deutschland" : Country,
            TaxId = TaxId,
            Status = "Aktiv",
            Notes = $"sevDesk:{ExternalId}"
        };
    }
}

public sealed class SevDeskInvoicePreview
{
    public bool IsSelected { get; set; } = true;
    public bool ExistsLocally { get; set; }
    public string ImportState { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string DueDate { get; set; } = "";
    public double Amount { get; set; }
    public double NetAmount { get; set; }
    public double VatAmount { get; set; }
    public double VatRate { get; set; } = 19;
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Offen";
    public string SourceStatus { get; set; } = "Offen";
    public string InvoiceType { get; set; } = "";

    public string AmountText => Amount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " €";
    public string NetAmountText => NetAmount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " €";
    public bool IsCancelled => string.Equals(Status, "Storniert", StringComparison.OrdinalIgnoreCase);

    public Invoice ToInvoice()
    {
        var description = string.IsNullOrWhiteSpace(Description)
            ? $"sevDesk Rechnung {InvoiceNumber}".Trim()
            : Description;

        if (!string.IsNullOrWhiteSpace(SourceStatus))
            description = $"{description} [{SourceStatus}]";

        return new Invoice
        {
            Customer = CustomerName,
            IssueDate = IssueDate,
            DueDate = DueDate,
            Amount = Amount,
            NetAmount = NetAmount,
            VatAmount = VatAmount,
            VatRate = VatRate,
            Description = $"{description} [sevDesk:{ExternalId}]",
            Status = Status,
            PaidAmount = Status == "Bezahlt" ? Amount : 0,
            PaidDate = Status == "Bezahlt" ? DateTime.Today.ToString("yyyy-MM-dd") : null
        };
    }
}

public sealed class SevDeskOfferPreview
{
    public bool IsSelected { get; set; } = true;
    public bool ExistsLocally { get; set; }
    public string ImportState { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string OfferNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string OfferDate { get; set; } = "";
    public string DateExpected { get; set; } = "";
    public double Amount { get; set; }
    public double Probability { get; set; } = 50;
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Offen";
    public string SourceStatus { get; set; } = "Offen";
    public string OrderType { get; set; } = "AN";
    public int PaymentDelay { get; set; } = 30;

    public string AmountText => Amount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " €";
    public bool IsRejected => string.Equals(Status, "Abgelehnt", StringComparison.OrdinalIgnoreCase);

    public Offer ToOffer()
    {
        var description = string.IsNullOrWhiteSpace(Description)
            ? $"sevDesk Angebot {OfferNumber}".Trim()
            : Description;

        if (!string.IsNullOrWhiteSpace(SourceStatus))
            description = $"{description} [{SourceStatus}]";

        return new Offer
        {
            OfferNumber = OfferNumber,
            OfferDate = OfferDate,
            DateExpected = DateExpected,
            Customer = CustomerName,
            Amount = Amount,
            Probability = Probability,
            Description = $"{description} [sevDesk:{ExternalId}]",
            Status = Status,
            PaymentDelay = PaymentDelay
        };
    }
}
