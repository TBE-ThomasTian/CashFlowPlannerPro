namespace CashFlowPlannerPro.Models;

/// <summary>
/// A read-only bank account known to an external source such as sevDesk.
/// Only a masked IBAN is persisted; banking credentials never belong here.
/// </summary>
public sealed class BankAccount
{
    public long Id { get; set; }
    public string SourceProvider { get; set; } = "sevdesk";
    public string ExternalAccountId { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string IbanMasked { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public double? Balance { get; set; }
    public string LastSync { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// A bank movement returned by an external source. SourceExternalId is the
/// provider's stable transaction ID and is required for idempotent imports.
/// </summary>
public sealed class BankTransaction
{
    public long Id { get; set; }
    public long BankAccountId { get; set; }
    public string SourceProvider { get; set; } = "sevdesk";
    public string SourceExternalId { get; set; } = "";
    public string EntryDate { get; set; } = "";
    public string ValueDate { get; set; } = "";
    public double Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Purpose { get; set; } = "";
    public string Payee { get; set; } = "";
    public string Status { get; set; } = "booked";
    public long? FixedCostTransactionId { get; set; }
    public bool IsSelected { get; set; } = true;
    public string AccountName { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public sealed class BankImportResult
{
    public long BankAccountId { get; internal set; }
    public int Selected { get; internal set; }
    public int Added { get; internal set; }
    public int Updated { get; internal set; }
    public int Skipped { get; internal set; }
}
