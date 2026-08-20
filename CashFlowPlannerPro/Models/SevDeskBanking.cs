using System.Globalization;

namespace CashFlowPlannerPro.Models;

/// <summary>
/// Read-only representation of a sevDesk payment/check account.
/// </summary>
public sealed class SevDeskCheckAccount
{
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// Display-safe IBAN value. The full IBAN is discarded while parsing the API response.
    /// </summary>
    public string IbanMasked { get; set; } = "";
    public string Currency { get; set; } = "";
    public double? Balance { get; set; }
    public DateTimeOffset? LastSync { get; set; }
    public string LastSyncRaw { get; set; } = "";
    public string Type { get; set; } = "";
    public string ImportType { get; set; } = "";
    public int? Status { get; set; }
    public string StatusRaw { get; set; } = "";
    public bool IsDefaultAccount { get; set; }
    public bool IsBaseAccount { get; set; }
    public bool AutoMapTransactions { get; set; }
    public bool AutoSyncTransactions { get; set; }

    public bool IsCurrencySupported =>
        string.Equals(Currency, "EUR", StringComparison.OrdinalIgnoreCase);

    public string CurrencyDisplay => string.IsNullOrWhiteSpace(Currency) ? "?" : Currency;

    public string IbanDisplay => IbanMasked;

    public string BalanceDisplay => Balance.HasValue
        ? Balance.Value.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " " + CurrencyDisplay
        : "–";

    public string LastSyncDisplay => LastSync.HasValue
        ? LastSync.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"))
        : "–";

    public string LastSyncIso => LastSync?.ToString("O", CultureInfo.InvariantCulture) ?? "";
}

/// <summary>
/// Read-only representation of a sevDesk check-account transaction.
/// Amounts use the same double convention as the application's Transaction model.
/// </summary>
public sealed class SevDeskCheckAccountTransaction
{
    /// <summary>The stable ID of the transaction inside sevDesk.</summary>
    public string ExternalId { get; set; } = "";

    public string AccountExternalId { get; set; } = "";
    public DateTimeOffset? ValueDate { get; set; }
    public string ValueDateRaw { get; set; } = "";
    public DateTimeOffset? EntryDate { get; set; }
    public string EntryDateRaw { get; set; } = "";
    public double Amount { get; set; }
    public bool HasValidAmount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string PaymtPurpose { get; set; } = "";
    public string PayeePayerName { get; set; } = "";
    public string EntryText { get; set; } = "";
    public int? Status { get; set; }
    public string StatusRaw { get; set; } = "";

    public bool IsCurrencySupported =>
        string.Equals(Currency, "EUR", StringComparison.OrdinalIgnoreCase);

    public bool IsCredit => Amount > 0;
    public bool IsDebit => Amount < 0;

    public string StatusLabel => Status switch
    {
        100 => "Erstellt",
        200 => "Zugeordnet",
        300 => "Privat",
        350 => "Automatisch gebucht",
        400 => "Gebucht",
        _ when !string.IsNullOrWhiteSpace(StatusRaw) => StatusRaw,
        _ => "Unbekannt"
    };

    public string ValueDateDisplay => ValueDate.HasValue
        ? ValueDate.Value.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"))
        : "–";

    public string ValueDateIso => ValueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    public string EntryDateIso => EntryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public string AmountDisplay =>
        Amount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) + " " +
        (string.IsNullOrWhiteSpace(Currency) ? "?" : Currency);
}
