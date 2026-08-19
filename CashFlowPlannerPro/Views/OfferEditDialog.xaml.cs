using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CashFlowPlannerPro.Models;
using CashFlowPlannerPro.Services;

namespace CashFlowPlannerPro.Views;

public partial class OfferEditDialog : Window
{
    private enum DiscountInputMode
    {
        Percent,
        Amount
    }

    private readonly Offer _workingOffer;
    private bool _synchronizingDiscount;
    private decimal _baseAmount;
    private decimal _discountPercent;
    private decimal _discountAmount;
    private DiscountInputMode _discountInputMode = DiscountInputMode.Percent;

    public Offer? ResultOffer { get; private set; }

    public OfferEditDialog(Offer offer, IEnumerable<string> customerNames)
    {
        InitializeComponent();
        _workingOffer = CopyOffer(offer);

        CbCustomer.ItemsSource = customerNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        LoadOffer();
        SaveBtn.ToolTip = TooltipService.Get("Btn_Save");
        CancelBtn.ToolTip = TooltipService.Get("Btn_Cancel");

        Loaded += (_, _) =>
        {
            FixEditableComboBox(CbCustomer);
            FixEditableComboBox(CbPaymentDelay);
            TbOfferNumber.Focus();
            TbOfferNumber.SelectAll();
        };
    }

    private void LoadOffer()
    {
        DialogTitle.Text = _workingOffer.Id > 0 ? "Angebot bearbeiten" : "Neues Angebot";

        TbOfferNumber.Text = _workingOffer.OfferNumber;
        CbCustomer.Text = _workingOffer.Customer;
        DpOfferDate.SelectedDate = ParseDate(_workingOffer.OfferDate) ?? DateTime.Today;
        DpExpectedDate.SelectedDate = ParseDate(_workingOffer.DateExpected) ?? DateTime.Today;
        TbProbability.Text = FormatNumber(ToDecimal(_workingOffer.Probability));
        CbStatus.SelectedItem = string.IsNullOrWhiteSpace(_workingOffer.Status) ? "Offen" : _workingOffer.Status;
        if (CbStatus.SelectedItem == null)
            CbStatus.SelectedItem = "Offen";
        TbDescription.Text = _workingOffer.Description;
        CbPaymentDelay.Text = _workingOffer.PaymentDelay.ToString(CultureInfo.CurrentCulture);
        UpdateDocumentContentButton();

        _baseAmount = RoundMoney(ToDecimal(_workingOffer.AmountBeforeDiscount));
        var finalAmount = RoundMoney(ToDecimal(_workingOffer.Amount));
        if (_baseAmount <= 0 && finalAmount > 0)
            _baseAmount = finalAmount;

        finalAmount = Math.Clamp(finalAmount, 0, _baseAmount);
        _discountAmount = RoundMoney(_baseAmount - finalAmount);

        var storedPercent = ToDecimal(_workingOffer.DiscountPercent);
        if (storedPercent is >= 0 and <= 100)
            _discountPercent = storedPercent;
        else
            _discountPercent = CalculatePercent(_baseAmount, _discountAmount);

        var amountFromStoredPercent = RoundMoney(_baseAmount * _discountPercent / 100m);
        if (Math.Abs(amountFromStoredPercent - _discountAmount) > 0.01m)
        {
            _discountPercent = CalculatePercent(_baseAmount, _discountAmount);
            _discountInputMode = DiscountInputMode.Amount;
        }

        _synchronizingDiscount = true;
        TbAmountBeforeDiscount.Text = FormatMoney(_baseAmount);
        TbDiscountPercent.Text = FormatNumber(_discountPercent);
        TbDiscountAmount.Text = FormatMoney(_discountAmount);
        TbFinalAmount.Text = FormatMoney(finalAmount);
        _synchronizingDiscount = false;
    }

    private void DocumentContent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DocumentContentEditDialog(_workingOffer.Content, "Angebot")
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.ResultContent != null)
        {
            _workingOffer.Content = dialog.ResultContent.DeepClone();
            UpdateDocumentContentButton();
        }
    }

    private void UpdateDocumentContentButton()
    {
        var count = _workingOffer.Content?.LineItems.Count ?? 0;
        DocumentContentBtn.Content = count == 1
            ? "Dokumentinhalt (1 Position)"
            : $"Dokumentinhalt ({count} Positionen)";
    }

    private void AmountBeforeDiscount_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_synchronizingDiscount || !TryParseFlexibleDecimal(TbAmountBeforeDiscount.Text, out var value) || value < 0)
            return;

        _baseAmount = RoundMoney(value);
        if (_discountInputMode == DiscountInputMode.Percent)
            RecalculateFromPercent(updatePercentText: false);
        else
            RecalculateFromAmount(updateAmountText: false);
    }

    private void DiscountPercent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_synchronizingDiscount || !TryParseFlexibleDecimal(TbDiscountPercent.Text, out var value))
            return;

        _discountInputMode = DiscountInputMode.Percent;
        _discountPercent = value;
        RecalculateFromPercent(updatePercentText: false);
    }

    private void DiscountAmount_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_synchronizingDiscount || !TryParseFlexibleDecimal(TbDiscountAmount.Text, out var value))
            return;

        _discountInputMode = DiscountInputMode.Amount;
        _discountAmount = RoundMoney(value);
        RecalculateFromAmount(updateAmountText: false);
    }

    private void RecalculateFromPercent(bool updatePercentText)
    {
        var previewPercent = Math.Clamp(_discountPercent, 0, 100);
        _discountAmount = RoundMoney(_baseAmount * previewPercent / 100m);
        var finalAmount = RoundMoney(_baseAmount - _discountAmount);

        _synchronizingDiscount = true;
        if (updatePercentText)
            TbDiscountPercent.Text = FormatNumber(_discountPercent);
        TbDiscountAmount.Text = FormatMoney(_discountAmount);
        TbFinalAmount.Text = FormatMoney(finalAmount);
        _synchronizingDiscount = false;
    }

    private void RecalculateFromAmount(bool updateAmountText)
    {
        _discountPercent = CalculatePercent(_baseAmount, _discountAmount);
        var finalAmount = RoundMoney(Math.Max(0, _baseAmount - _discountAmount));

        _synchronizingDiscount = true;
        TbDiscountPercent.Text = FormatNumber(_discountPercent);
        if (updateAmountText)
            TbDiscountAmount.Text = FormatMoney(_discountAmount);
        TbFinalAmount.Text = FormatMoney(finalAmount);
        _synchronizingDiscount = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbOfferNumber.Text))
        {
            ModernMessageBox.ShowError("Bitte geben Sie eine Angebotsnummer ein.", "Pflichtfeld");
            TbOfferNumber.Focus();
            return;
        }

        if (!TryParseFlexibleDecimal(TbAmountBeforeDiscount.Text, out var baseAmount) || baseAmount < 0)
        {
            ModernMessageBox.ShowError("Bitte geben Sie einen gültigen Ausgangsbetrag ab 0 € ein.", "Ungültiger Betrag");
            TbAmountBeforeDiscount.Focus();
            return;
        }
        baseAmount = RoundMoney(baseAmount);

        decimal discountPercent;
        decimal discountAmount;
        if (_discountInputMode == DiscountInputMode.Percent)
        {
            if (!TryParseFlexibleDecimal(TbDiscountPercent.Text, out discountPercent) || discountPercent is < 0 or > 100)
            {
                ModernMessageBox.ShowError("Der Rabatt muss zwischen 0 und 100 % liegen.", "Ungültiger Rabatt");
                TbDiscountPercent.Focus();
                return;
            }
            discountAmount = RoundMoney(baseAmount * discountPercent / 100m);
        }
        else
        {
            if (!TryParseFlexibleDecimal(TbDiscountAmount.Text, out discountAmount) ||
                discountAmount < 0 || discountAmount > baseAmount)
            {
                ModernMessageBox.ShowError(
                    "Der Rabattbetrag muss zwischen 0 € und dem Ausgangsbetrag liegen.",
                    "Ungültiger Rabatt");
                TbDiscountAmount.Focus();
                return;
            }
            discountAmount = RoundMoney(discountAmount);
            discountPercent = CalculatePercent(baseAmount, discountAmount);
        }

        if (!TryParseFlexibleDecimal(TbProbability.Text, out var probability) || probability is < 0 or > 100)
        {
            ModernMessageBox.ShowError("Die Wahrscheinlichkeit muss zwischen 0 und 100 % liegen.", "Ungültige Wahrscheinlichkeit");
            TbProbability.Focus();
            return;
        }

        if (!int.TryParse(CbPaymentDelay.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var paymentDelay) || paymentDelay < 0)
        {
            ModernMessageBox.ShowError("Bitte geben Sie ein gültiges Zahlungsziel ab 0 Tagen ein.", "Ungültiges Zahlungsziel");
            CbPaymentDelay.Focus();
            return;
        }

        _workingOffer.OfferNumber = TbOfferNumber.Text.Trim();
        _workingOffer.OfferDate = DpOfferDate.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        _workingOffer.DateExpected = DpExpectedDate.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        _workingOffer.Customer = CbCustomer.Text.Trim();
        _workingOffer.AmountBeforeDiscount = (double)baseAmount;
        _workingOffer.DiscountPercent = (double)discountPercent;
        _workingOffer.Amount = (double)RoundMoney(baseAmount - discountAmount);
        _workingOffer.Probability = (double)probability;
        _workingOffer.Status = CbStatus.SelectedItem?.ToString() ?? "Offen";
        _workingOffer.Description = TbDescription.Text.Trim();
        _workingOffer.PaymentDelay = paymentDelay;

        ResultOffer = _workingOffer;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }

    private static void FixEditableComboBox(ComboBox comboBox)
    {
        if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is TextBox textBox)
        {
            textBox.Foreground = Brushes.White;
            textBox.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x40));
            textBox.CaretBrush = Brushes.White;
        }
    }

    private void DatePicker_FixStyle(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker datePicker)
            return;

        if (datePicker.Template.FindName("PART_TextBox", datePicker) is not System.Windows.Controls.Primitives.DatePickerTextBox textBox)
            return;

        textBox.Foreground = Brushes.White;
        textBox.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x40));
        textBox.BorderThickness = new Thickness(0);

        if (textBox.Template?.FindName("PART_Watermark", textBox) is ContentControl watermark)
            watermark.Visibility = Visibility.Collapsed;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) ||
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            ? date
            : null;

    private static decimal CalculatePercent(decimal baseAmount, decimal discountAmount) =>
        baseAmount <= 0
            ? 0
            : Math.Round(discountAmount / baseAmount * 100m, 6, MidpointRounding.AwayFromZero);

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal ToDecimal(double value)
    {
        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return 0;
        }
    }

    private static string FormatMoney(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    private static string FormatNumber(decimal value) => value.ToString("0.######", CultureInfo.CurrentCulture);

    private static bool TryParseFlexibleDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim()
            .Replace("€", "", StringComparison.Ordinal)
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("\u00A0", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal);

        var commaIndex = cleaned.LastIndexOf(',');
        var dotIndex = cleaned.LastIndexOf('.');
        if (commaIndex >= 0 && dotIndex >= 0)
        {
            var decimalSeparator = commaIndex > dotIndex ? ',' : '.';
            var groupingSeparator = decimalSeparator == ',' ? "." : ",";
            cleaned = cleaned.Replace(groupingSeparator, "", StringComparison.Ordinal)
                .Replace(decimalSeparator, '.');
        }
        else if (commaIndex >= 0 || dotIndex >= 0)
        {
            var separator = commaIndex >= 0 ? ',' : '.';
            var separatorIndex = Math.Max(commaIndex, dotIndex);
            var occurrenceCount = cleaned.Count(character => character == separator);
            var digitsAfterSeparator = cleaned.Length - separatorIndex - 1;
            var currentDecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var groups = cleaned.Split(separator);
            var isCurrentGrouping = separator.ToString() != currentDecimalSeparator &&
                                    groups.Length > 1 &&
                                    groups.Skip(1).All(group => group.Length == 3);

            if (isCurrentGrouping)
                cleaned = cleaned.Replace(separator.ToString(), "", StringComparison.Ordinal);
            else if (occurrenceCount == 1)
                cleaned = cleaned.Replace(separator, '.');
            else
            {
                cleaned = string.Concat(groups[..^1]) + "." + groups[^1];
            }
        }

        return decimal.TryParse(
            cleaned,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static Offer CopyOffer(Offer source) => new()
    {
        Id = source.Id,
        OfferNumber = source.OfferNumber,
        OfferDate = source.OfferDate,
        DateExpected = source.DateExpected,
        Customer = source.Customer,
        CustomerId = source.CustomerId,
        AmountBeforeDiscount = source.AmountBeforeDiscount,
        DiscountPercent = source.DiscountPercent,
        Amount = source.Amount,
        Probability = source.Probability,
        Description = source.Description,
        Status = source.Status,
        PaymentDelay = source.PaymentDelay,
        PdfPath = source.PdfPath,
        CreatedAt = source.CreatedAt,
        ProjectId = source.ProjectId,
        ProjectNumber = source.ProjectNumber,
        Content = source.Content?.DeepClone() ?? new DocumentContent()
    };
}
