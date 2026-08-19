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
    public const int CustomerNumberMaxLength = 100;

    public bool IsSelected { get; set; } = true;
    public bool ExistsLocally { get; set; }
    public bool HasImportConflict { get; set; }
    public bool CanImport =>
        !HasImportConflict
        && !string.IsNullOrWhiteSpace(ExternalId)
        && (CustomerNumber ?? "").Trim().Length <= CustomerNumberMaxLength;
    public string ImportState { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string CustomerNumber { get; set; } = "";
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
        var normalizedExternalId = (ExternalId ?? "").Trim();
        var normalizedCustomerNumber = (CustomerNumber ?? "").Trim();

        if (string.IsNullOrWhiteSpace(normalizedExternalId))
            throw new InvalidOperationException("Der sevDesk-Kontakt kann nicht importiert werden, weil die sevDesk-ID fehlt.");

        if (normalizedCustomerNumber.Length > CustomerNumberMaxLength)
        {
            throw new InvalidOperationException(
                $"Der sevDesk-Kontakt kann nicht importiert werden, weil die Kundennummer mehr als {CustomerNumberMaxLength} Zeichen hat.");
        }

        return new Customer
        {
            CustomerNumber = normalizedCustomerNumber,
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
            Notes = $"sevDesk:{normalizedExternalId}"
        };
    }
}

public sealed class SevDeskInvoicePreview
{
    public bool IsSelected { get; set; } = true;
    public bool ExistsLocally { get; set; }
    public bool HasImportConflict { get; set; }
    public bool CanImport => !HasImportConflict && IsCurrencySupported;
    public string ImportState { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string InvoiceNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string DueDate { get; set; } = "";
    public double Amount { get; set; }
    public double NetAmount { get; set; }
    public double VatAmount { get; set; }
    public double VatRate { get; set; }
    public double PaidAmount { get; set; }
    public string? PaidDate { get; set; }
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Offen";
    public string SourceStatus { get; set; } = "Offen";
    public string InvoiceType { get; set; } = "";
    public DocumentContent Content { get; set; } = new();

    public bool IsCurrencySupported => string.Equals(Currency, "EUR", StringComparison.OrdinalIgnoreCase);
    public string CurrencyDisplay => string.IsNullOrWhiteSpace(Currency) ? "?" : Currency;
    public string AmountText => Amount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " " + CurrencyDisplay;
    public string NetAmountText => NetAmount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " " + CurrencyDisplay;
    public bool IsCancelled => string.Equals(Status, "Storniert", StringComparison.OrdinalIgnoreCase);

    public Invoice ToInvoice()
    {
        EnsureCurrencySupported();
        return new Invoice
        {
            InvoiceNumber = InvoiceNumber,
            Customer = CustomerName,
            IssueDate = IssueDate,
            DueDate = DueDate,
            Amount = Amount,
            NetAmount = NetAmount,
            VatAmount = VatAmount,
            VatRate = VatRate,
            Description = string.IsNullOrWhiteSpace(Description) ? Content.Header : Description,
            Status = Status,
            PaidAmount = PaidAmount,
            PaidDate = PaidDate,
            Content = Content.DeepClone()
        };
    }

    private void EnsureCurrencySupported()
    {
        if (!IsCurrencySupported)
        {
            throw new InvalidOperationException(
                $"Die sevDesk-Rechnung {InvoiceNumber} verwendet die Währung {CurrencyDisplay}. CashFlow Planner kann derzeit nur EUR-Belege sicher importieren.");
        }
    }
}

public sealed class SevDeskOfferPreview
{
    public bool IsSelected { get; set; } = true;
    public bool ExistsLocally { get; set; }
    public bool HasImportConflict { get; set; }
    public bool CanImport => !HasImportConflict && IsCurrencySupported;
    public string ImportState { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string OfferNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string OfferDate { get; set; } = "";
    public string DateExpected { get; set; } = "";
    public double Amount { get; set; }
    public double AmountBeforeDiscount { get; set; }
    public double DiscountPercent { get; set; }
    public double Probability { get; set; } = 50;
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Offen";
    public string SourceStatus { get; set; } = "Offen";
    public string OrderType { get; set; } = "AN";
    public int? PaymentDelay { get; set; }
    public DocumentContent Content { get; set; } = new();

    public bool IsCurrencySupported => string.Equals(Currency, "EUR", StringComparison.OrdinalIgnoreCase);
    public string CurrencyDisplay => string.IsNullOrWhiteSpace(Currency) ? "?" : Currency;
    public string AmountText => Amount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " " + CurrencyDisplay;
    public bool IsRejected => string.Equals(Status, "Abgelehnt", StringComparison.OrdinalIgnoreCase);

    public Offer ToOffer()
    {
        EnsureCurrencySupported();
        return new Offer
        {
            OfferNumber = OfferNumber,
            OfferDate = OfferDate,
            DateExpected = DateExpected,
            Customer = CustomerName,
            AmountBeforeDiscount = AmountBeforeDiscount,
            DiscountPercent = DiscountPercent,
            Amount = Amount,
            Probability = Probability,
            Description = string.IsNullOrWhiteSpace(Description) ? Content.Header : Description,
            Status = Status,
            PaymentDelay = PaymentDelay ?? 30,
            Content = Content.DeepClone()
        };
    }

    private void EnsureCurrencySupported()
    {
        if (!IsCurrencySupported)
        {
            throw new InvalidOperationException(
                $"Das sevDesk-Angebot {OfferNumber} verwendet die Währung {CurrencyDisplay}. CashFlow Planner kann derzeit nur EUR-Belege sicher importieren.");
        }
    }
}
