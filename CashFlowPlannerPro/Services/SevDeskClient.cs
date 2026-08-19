using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Services;

public static class SevDeskClient
{
    private const string BaseUrl = "https://my.sevdesk.de/api/v1/";
    private static readonly Regex HtmlBreakRegex = new(
        @"<(?:br|hr)\b[^>]*?/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex HtmlBlockEndRegex = new(
        @"</(?:p|div|li|tr|h[1-6]|ul|ol|table|section|article)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex HtmlListItemRegex = new(
        @"<li\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex HorizontalWhitespaceRegex = new(
        @"[\t\p{Zs}]+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex PaymentDaysRegex = new(
        @"^\s*(?<days>\d+)\s*(?:Tag(?:e)?|days?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static async Task TestConnectionAsync(string apiToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(apiToken, "Contact?limit=1&countAll=true", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public static async Task<SevDeskImportPreview> LoadImportPreviewAsync(string apiToken, CancellationToken cancellationToken = default)
    {
        var contactsTask = FetchObjectsAsync(apiToken, "Contact", cancellationToken);
        var invoicesTask = FetchObjectsAsync(apiToken, "Invoice?embed=contact", cancellationToken);
        var ordersTask = FetchObjectsAsync(apiToken, "Order?embed=contact", cancellationToken);
        var invoicePositionsTask = FetchObjectsAsync(apiToken, "InvoicePos?embed=unity", cancellationToken);
        var orderPositionsTask = FetchObjectsAsync(apiToken, "OrderPos?embed=unity", cancellationToken);
        var addressesTask = TryFetchObjectsAsync(apiToken, "ContactAddress?embed=country", cancellationToken);
        var communicationWaysTask = TryFetchObjectsAsync(apiToken, "CommunicationWay", cancellationToken);

        await Task.WhenAll(
            contactsTask,
            invoicesTask,
            ordersTask,
            invoicePositionsTask,
            orderPositionsTask,
            addressesTask,
            communicationWaysTask);

        var addressesByContactId = addressesTask.Result
            .Select(ParseContactAddress)
            .Where(x => !string.IsNullOrWhiteSpace(x.ContactId))
            .GroupBy(x => x.ContactId)
            .ToDictionary(g => g.Key, g => SelectPreferredAddress(g));

        var communicationWaysByContactId = communicationWaysTask.Result
            .Select(ParseCommunicationWay)
            .Where(x => !string.IsNullOrWhiteSpace(x.ContactId))
            .GroupBy(x => x.ContactId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var invoicePositionsByInvoiceId = GroupLineItemsByParent(invoicePositionsTask.Result, "invoice");
        var orderPositionsByOrderId = GroupLineItemsByParent(orderPositionsTask.Result, "order");

        return new SevDeskImportPreview
        {
            Contacts = contactsTask.Result
                .Select(item =>
                {
                    var contactId = GetString(item, "id");
                    addressesByContactId.TryGetValue(contactId, out var address);
                    communicationWaysByContactId.TryGetValue(contactId, out var communicationWays);
                    return ParseContact(item, address, communicationWays);
                })
                .Where(c => !string.IsNullOrWhiteSpace(c.DisplayName))
                .ToList(),
            Invoices = invoicesTask.Result
                .Select(item => ParseInvoice(
                    item,
                    GetLineItems(invoicePositionsByInvoiceId, GetString(item, "id"))))
                .ToList(),
            Offers = ordersTask.Result
                .Select(item => ParseOffer(
                    item,
                    GetLineItems(orderPositionsByOrderId, GetString(item, "id"))))
                .Where(o => o != null)
                .Cast<SevDeskOfferPreview>()
                .ToList()
        };
    }

    private static async Task<List<JsonElement>> TryFetchObjectsAsync(string apiToken, string resource, CancellationToken cancellationToken)
    {
        try
        {
            return await FetchObjectsAsync(apiToken, resource, cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    private static async Task<List<JsonElement>> FetchObjectsAsync(string apiToken, string resource, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var objects = new List<JsonElement>();
        int offset = 0;
        int? total = null;

        while (!total.HasValue || offset < total.Value)
        {
            var separator = resource.Contains('?') ? "&" : "?";
            using var response = await SendAsync(apiToken, $"{resource}{separator}limit={pageSize}&offset={offset}&countAll=true", cancellationToken);
            var json = await EnsureSuccessAndReadAsync(response, cancellationToken);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("objects", out var array) || array.ValueKind != JsonValueKind.Array)
                break;

            objects.AddRange(array.EnumerateArray().Select(x => x.Clone()));
            total = TryGetInt(doc.RootElement, "total");
            if (array.GetArrayLength() < pageSize)
                break;

            offset += pageSize;
        }

        return objects;
    }

    private static async Task<HttpResponseMessage> SendAsync(string apiToken, string relativeUrl, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(BaseUrl), relativeUrl));
        request.Headers.TryAddWithoutValidation("Authorization", apiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("CashFlowPlannerPro/2.0");
        return await Http.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(BuildErrorMessage(response, payload));
    }

    private static async Task<string> EnsureSuccessAndReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(BuildErrorMessage(response, payload));
        return payload;
    }

    private static string BuildErrorMessage(HttpResponseMessage response, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return $"{(int)response.StatusCode} {response.ReasonPhrase}";

        return $"{(int)response.StatusCode} {response.ReasonPhrase}: {payload}";
    }

    private static SevDeskContactPreview ParseContact(JsonElement item) =>
        ParseContact(item, null, null);

    private static SevDeskContactPreview ParseContact(
        JsonElement item,
        ContactAddressData? address,
        IReadOnlyCollection<CommunicationWayData>? communicationWays)
    {
        var company = GetString(item, "name");
        var firstName = GetString(item, "surename", "firstName");
        var lastName = GetString(item, "familyname", "lastName");
        var contactName = JoinNonEmpty(GetString(item, "academicTitle"), firstName, lastName);
        var email = FirstCommunicationValue(communicationWays, "EMAIL");
        var phone = FirstCommunicationValue(communicationWays, "PHONE", "MOBILE");

        return new SevDeskContactPreview
        {
            ExternalId = GetString(item, "id"),
            CustomerNumber = GetString(item, "customerNumber"),
            Company = company,
            ContactName = string.IsNullOrWhiteSpace(contactName) ? JoinNonEmpty(address?.Name ?? "", address?.Name2 ?? "") : contactName,
            Email = string.IsNullOrWhiteSpace(email) ? GetString(item, "email") : email,
            Phone = string.IsNullOrWhiteSpace(phone) ? GetString(item, "phone", "mobile") : phone,
            Street = address?.Street ?? GetString(item, "street"),
            ZipCode = address?.ZipCode ?? GetString(item, "zip"),
            City = address?.City ?? GetString(item, "city"),
            Country = address?.Country ?? GetString(item, "country", "countryCode"),
            TaxId = GetString(item, "vatNumber", "taxNumber")
        };
    }

    private static SevDeskInvoicePreview ParseInvoice(
        JsonElement item,
        IReadOnlyList<DocumentLineItem> lineItems)
    {
        var contact = GetNestedObject(item, "contact");
        var customerName = contact.HasValue
            ? ParseContact(contact.Value).DisplayName
            : GetString(item, "addressName", "name");

        var externalId = GetString(item, "id");
        var currency = NormalizeCurrency(GetString(item, "currency"));
        var invoiceNumber = GetString(item, "invoiceNumber", "number");
        var header = HtmlToPlainText(GetFirstNonEmptyString(item, "header", "text"));
        var preText = HtmlToPlainText(GetString(item, "headText"));
        var postText = HtmlToPlainText(GetString(item, "footText"));
        var internalNote = HtmlToPlainText(GetString(item, "customerInternalNote"));
        var issueDate = NormalizeDate(GetString(item, "invoiceDate", "create"));
        var dueDate = NormalizeDate(GetString(item, "dueDate"));
        if (string.IsNullOrWhiteSpace(dueDate)
            && TryParseNormalizedDate(issueDate, out var issueDateValue)
            && GetStrictInt(item, "timeToPay") is int timeToPay
            && timeToPay >= 0)
        {
            dueDate = issueDateValue.AddDays(timeToPay).ToString("yyyy-MM-dd");
        }

        var invoiceType = GetString(item, "invoiceType");
        var sourceStatus = BuildInvoiceStatusLabel(item, dueDate, invoiceType);
        var grossAmount = GetDouble(item, "sumGross", "totalGross", "invoiceSum");
        var netAmount = GetDouble(item, "sumNet", "totalNet");
        var parsedVatAmount = GetNullableDouble(item, "sumTax", "taxAmount", "vatAmount");
        var vatAmount = parsedVatAmount ?? 0;
        if (!parsedVatAmount.HasValue && Math.Abs(grossAmount) > 0.000001 && Math.Abs(netAmount) > 0.000001)
            vatAmount = Math.Round(grossAmount - netAmount, 2);
        var vatRate = DetermineVatRate(item, lineItems, netAmount, vatAmount);
        var mappedStatus = MapInvoiceStatus(sourceStatus);
        var paidAmount = GetNullableDouble(item, "paidAmount")
            ?? (string.Equals(mappedStatus, "Bezahlt", StringComparison.Ordinal) ? grossAmount : 0);
        var paidDate = NormalizeDate(GetString(item, "payDate"));

        var content = DocumentContentMerge.PrepareImported(new DocumentContent
        {
            Header = header,
            PreText = preText,
            PostText = postText,
            InternalNote = internalNote,
            SourceProvider = "sevDesk",
            SourceEntityType = "Invoice",
            SourceExternalId = externalId,
            LineItems = new System.Collections.ObjectModel.ObservableCollection<DocumentLineItem>(
                lineItems.Select(line => line.DeepClone()))
        });

        return new SevDeskInvoicePreview
        {
            ExternalId = externalId,
            Currency = currency,
            InvoiceNumber = invoiceNumber,
            CustomerName = customerName,
            IssueDate = issueDate,
            DueDate = dueDate,
            Amount = grossAmount,
            NetAmount = netAmount,
            VatAmount = vatAmount,
            VatRate = vatRate,
            PaidAmount = paidAmount,
            PaidDate = string.IsNullOrWhiteSpace(paidDate) ? null : paidDate,
            Description = header,
            SourceStatus = sourceStatus,
            InvoiceType = invoiceType,
            Status = mappedStatus,
            Content = content
        };
    }

    private static SevDeskOfferPreview? ParseOffer(
        JsonElement item,
        IReadOnlyList<DocumentLineItem> lineItems)
    {
        var orderType = GetString(item, "orderType");
        if (!string.Equals(orderType, "AN", StringComparison.OrdinalIgnoreCase))
            return null;

        var contact = GetNestedObject(item, "contact");
        var customerName = contact.HasValue
            ? ParseContact(contact.Value).DisplayName
            : GetString(item, "addressName", "name");

        var externalId = GetString(item, "id");
        var currency = NormalizeCurrency(GetString(item, "currency"));
        var offerNumber = GetString(item, "orderNumber", "number");
        var offerDate = NormalizeDate(GetString(item, "orderDate", "create"));
        var expectedDate = NormalizeDate(GetFirstNonEmptyString(item, "deliveryDate", "deliveryDateUntil"));

        var rawStatus = GetString(item, "status");
        var sourceStatus = BuildOrderStatusLabel(rawStatus);
        var header = HtmlToPlainText(GetString(item, "header"));
        var preText = HtmlToPlainText(GetString(item, "headText"));
        var postText = HtmlToPlainText(GetString(item, "footText"));
        var internalNote = HtmlToPlainText(GetString(item, "customerInternalNote"));
        var grossAmount = GetDouble(item, "sumGross", "totalGross");
        var (amountBeforeDiscount, discountPercent) = DetermineOfferDiscount(item, lineItems, grossAmount);
        var paymentDelay = ParsePaymentDelay(item);
        var content = DocumentContentMerge.PrepareImported(new DocumentContent
        {
            Header = header,
            PreText = preText,
            PostText = postText,
            InternalNote = internalNote,
            SourceProvider = "sevDesk",
            SourceEntityType = "Order",
            SourceExternalId = externalId,
            LineItems = new System.Collections.ObjectModel.ObservableCollection<DocumentLineItem>(
                lineItems.Select(line => line.DeepClone()))
        });

        return new SevDeskOfferPreview
        {
            ExternalId = externalId,
            Currency = currency,
            OfferNumber = offerNumber,
            CustomerName = customerName,
            OfferDate = offerDate,
            DateExpected = expectedDate,
            Amount = grossAmount,
            AmountBeforeDiscount = amountBeforeDiscount,
            DiscountPercent = discountPercent,
            Probability = DetermineOfferProbability(rawStatus),
            Description = header,
            SourceStatus = sourceStatus,
            OrderType = orderType,
            PaymentDelay = paymentDelay,
            Status = MapOfferStatus(rawStatus),
            Content = content
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<DocumentLineItem>> GroupLineItemsByParent(
        IEnumerable<JsonElement> positions,
        string parentPropertyName)
    {
        var entries = positions
            .Select(position => new
            {
                ParentId = GetNestedObjectId(position, parentPropertyName),
                Position = position
            })
            .ToList();

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.ParentId)))
        {
            throw new InvalidOperationException(
                $"sevDesk lieferte eine Dokumentposition ohne '{parentPropertyName}'-ID.");
        }

        return entries
            .GroupBy(entry => entry.ParentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DocumentLineItem>)group
                    .Select((entry, index) => ParseLineItem(entry.Position, index))
                    .OrderBy(line => line.SortOrder)
                    .ToList(),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<DocumentLineItem> GetLineItems(
        IReadOnlyDictionary<string, IReadOnlyList<DocumentLineItem>> lineItemsByParentId,
        string parentId)
    {
        return !string.IsNullOrWhiteSpace(parentId)
            && lineItemsByParentId.TryGetValue(parentId, out var lineItems)
                ? lineItems
                : [];
    }

    private static DocumentLineItem ParseLineItem(JsonElement item, int fallbackSortOrder)
    {
        var quantity = GetNullableDouble(item, "quantity") ?? 1;
        var discountPercent = GetNullableDouble(item, "discount") ?? 0;
        var taxRate = GetNullableDouble(item, "taxRate") ?? 0;
        var priceNet = GetNullableDouble(item, "priceNet");
        var priceGross = GetNullableDouble(item, "priceGross");
        var rawPrice = GetNullableDouble(item, "price") ?? 0;
        var grossFactor = 1 + taxRate / 100d;
        var unitPrice = priceNet
            ?? (priceGross.HasValue && Math.Abs(grossFactor) > 0.000001
                ? priceGross.Value / grossFactor
                : rawPrice);
        var discountFactor = Math.Max(0, 1 - discountPercent / 100d);

        // sevDesk's sum*Accounting values belong to the accounting currency,
        // whereas priceNet/priceGross belong to the document currency. Using
        // both would mix currency levels for foreign-currency documents.
        // Calculate line totals exclusively from document-currency unit prices.
        var netAmount = Math.Round(quantity * unitPrice * discountFactor, 2);
        var grossAmount = priceGross.HasValue
            ? Math.Round(quantity * priceGross.Value * discountFactor, 2)
            : Math.Round(netAmount * (1 + taxRate / 100d), 2);

        var positionNumber = GetFirstNonEmptyString(item, "positionNumber", "pos");
        var parsedSortOrder = GetStrictInt(item, "positionNumber", "pos") ?? fallbackSortOrder;
        var unit = GetNestedString(item, "unity", "name", "abbreviation", "translationCode", "key");
        if (string.IsNullOrWhiteSpace(unit))
            unit = GetFirstNonEmptyString(item, "unityName", "unit");

        return new DocumentLineItem
        {
            SourceItemId = NullIfWhiteSpace(GetString(item, "id")),
            SortOrder = parsedSortOrder,
            PositionNumber = string.IsNullOrWhiteSpace(positionNumber)
                ? (fallbackSortOrder + 1).ToString(CultureInfo.InvariantCulture)
                : positionNumber,
            Name = HtmlToPlainText(GetString(item, "name")),
            Description = HtmlToPlainText(GetString(item, "text")),
            Quantity = quantity,
            Unit = HtmlToPlainText(unit),
            UnitPrice = unitPrice,
            DiscountPercent = discountPercent,
            TaxRate = taxRate,
            NetAmount = netAmount,
            GrossAmount = grossAmount,
            IsOptional = GetBoolean(item, "optional")
        };
    }

    private static JsonElement? GetNestedObject(JsonElement item, string propertyName)
    {
        if (item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object)
            return value;
        return null;
    }

    private static string GetNestedObjectId(JsonElement item, string propertyName)
    {
        var nested = GetNestedObject(item, propertyName);
        return nested.HasValue ? GetString(nested.Value, "id") : "";
    }

    private static string GetNestedString(JsonElement item, string propertyName, params string[] nestedPropertyNames)
    {
        var nested = GetNestedObject(item, propertyName);
        return nested.HasValue ? GetString(nested.Value, nestedPropertyNames) : "";
    }

    private static string GetString(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!item.TryGetProperty(propertyName, out var value))
                continue;

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString() ?? "";
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.ToString();
            }
        }

        return "";
    }

    private static string GetFirstNonEmptyString(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetString(item, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }

    private static double GetDouble(JsonElement item, params string[] propertyNames)
        => GetNullableDouble(item, propertyNames) ?? 0;

    private static double? GetNullableDouble(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!item.TryGetProperty(propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var direct)
                && double.IsFinite(direct))
                return direct;

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                && double.IsFinite(parsed))
                return parsed;
        }

        return null;
    }

    private static int? TryGetInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var direct))
            return direct;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    private static int? GetInt(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = TryGetInt(item, propertyName);
            if (value.HasValue)
                return value;
        }

        return null;
    }

    private static int? GetStrictInt(JsonElement item, params string[] propertyNames) =>
        GetInt(item, propertyNames);

    private static int? ParsePaymentDelay(JsonElement order)
    {
        var explicitDays = GetStrictInt(order, "timeToPay");
        if (explicitDays >= 0)
            return explicitDays;

        var paymentTerms = GetString(order, "paymentTerms");
        var match = PaymentDaysRegex.Match(paymentTerms);
        return match.Success
            && int.TryParse(match.Groups["days"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days)
                ? days
                : null;
    }

    private static bool GetBoolean(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String => IsTruthy(value.GetString() ?? ""),
            _ => false
        };
    }

    private static string NormalizeCurrency(string value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

    private static string NormalizeDate(string value)
    {
        if (!TryParseNormalizedDate(value, out var parsed))
            return "";

        return parsed.ToString("yyyy-MM-dd");
    }

    private static bool TryParseNormalizedDate(string value, out DateTime parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("0000-00-00", StringComparison.Ordinal)
            || string.Equals(value.Trim(), "0", StringComparison.Ordinal))
        {
            return false;
        }

        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd.MM.yyyy",
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
            "yyyy-MM-dd HH:mm:ss"
        };

        if (DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out parsed))
        {
            return true;
        }

        return DateTime.TryParse(
                value,
                CultureInfo.GetCultureInfo("de-DE"),
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out parsed)
            || DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out parsed);
    }

    private static double DetermineVatRate(
        JsonElement invoice,
        IReadOnlyList<DocumentLineItem> lineItems,
        double netAmount,
        double vatAmount)
    {
        var applicableLineItems = lineItems.Where(line => !line.IsOptional).ToList();
        if (applicableLineItems.Count > 0)
        {
            var firstRate = applicableLineItems[0].TaxRate;
            if (applicableLineItems.All(line => Math.Abs(line.TaxRate - firstRate) <= 0.000001))
                return firstRate;
        }

        if (Math.Abs(netAmount) > 0.000001)
            return Math.Round(vatAmount / netAmount * 100, 4);

        return GetNullableDouble(invoice, "taxRate") ?? 0;
    }

    private static (double AmountBeforeDiscount, double DiscountPercent) DetermineOfferDiscount(
        JsonElement order,
        IReadOnlyList<DocumentLineItem> lineItems,
        double grossAmount)
    {
        var documentDiscount = GetNullableDouble(order, "sumDiscounts", "sumDiscountsForeignCurrency");
        if (documentDiscount.HasValue && Math.Abs(documentDiscount.Value) > 0.000001)
        {
            var before = grossAmount + Math.Abs(documentDiscount.Value);
            if (before > 0.000001)
            {
                return (
                    Math.Round(before, 2, MidpointRounding.AwayFromZero),
                    Math.Round(Math.Abs(documentDiscount.Value) / before * 100, 4, MidpointRounding.AwayFromZero));
            }
        }

        var applicableItems = lineItems.Where(line => !line.IsOptional).ToList();
        if (applicableItems.Count > 0
            && applicableItems.Any(line => line.DiscountPercent > 0.000001)
            && applicableItems.All(line => line.DiscountPercent >= 0 && line.DiscountPercent < 100))
        {
            var afterFromItems = applicableItems.Sum(line => line.GrossAmount);
            var beforeFromItems = applicableItems.Sum(line =>
                line.GrossAmount / (1 - line.DiscountPercent / 100d));
            var matchingTolerance = Math.Max(0.05, Math.Abs(grossAmount) * 0.001);

            if (beforeFromItems > afterFromItems + 0.000001
                && Math.Abs(afterFromItems - grossAmount) <= matchingTolerance)
            {
                return (
                    Math.Round(beforeFromItems, 2, MidpointRounding.AwayFromZero),
                    Math.Round((beforeFromItems - grossAmount) / beforeFromItems * 100, 4, MidpointRounding.AwayFromZero));
            }
        }

        return (grossAmount, 0);
    }

    private static string BuildInvoiceStatusLabel(JsonElement item, string dueDate, string invoiceType)
    {
        var raw = GetString(item, "status", "invoiceStatus");
        if (string.Equals(invoiceType, "SR", StringComparison.OrdinalIgnoreCase))
            return "Storniert";

        if (raw.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("storno", StringComparison.OrdinalIgnoreCase))
            return "Storniert";

        if (int.TryParse(raw, out var numeric))
        {
            return numeric switch
            {
                50 => "Deaktiviert",
                100 => "Entwurf",
                200 => IsOverdue(dueDate) ? "Überfällig" : "Offen",
                750 => "Teilbezahlt",
                1000 => "Bezahlt",
                _ => "Offen"
            };
        }

        if (raw.Contains("paid", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("bezahlt", StringComparison.OrdinalIgnoreCase))
            return "Bezahlt";

        if (raw.Contains("draft", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("entwurf", StringComparison.OrdinalIgnoreCase))
            return "Entwurf";

        if (raw.Contains("partial", StringComparison.OrdinalIgnoreCase))
            return "Teilbezahlt";

        return IsOverdue(dueDate) ? "Überfällig" : "Offen";
    }

    private static string BuildOrderStatusLabel(string rawStatus)
    {
        if (int.TryParse(rawStatus, out var numeric))
        {
            return numeric switch
            {
                100 => "Entwurf",
                200 => "Gesendet",
                300 => "Abgelehnt",
                500 => "Angenommen",
                750 => "Teilweise berechnet",
                1000 => "Berechnet",
                _ => "Offen"
            };
        }

        if (rawStatus.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            || rawStatus.Contains("reject", StringComparison.OrdinalIgnoreCase))
            return "Abgelehnt";

        if (rawStatus.Contains("accept", StringComparison.OrdinalIgnoreCase))
            return "Angenommen";

        if (rawStatus.Contains("draft", StringComparison.OrdinalIgnoreCase))
            return "Entwurf";

        return "Offen";
    }

    private static string MapInvoiceStatus(string sourceStatus) =>
        sourceStatus switch
        {
            "Bezahlt" => "Bezahlt",
            "Storniert" => "Storniert",
            "Überfällig" => "Überfällig",
            _ => "Offen"
        };

    private static string MapOfferStatus(string rawStatus)
    {
        if (!int.TryParse(rawStatus, out var numeric))
        {
            if (rawStatus.Contains("reject", StringComparison.OrdinalIgnoreCase)
                || rawStatus.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                return "Abgelehnt";

            if (rawStatus.Contains("accept", StringComparison.OrdinalIgnoreCase))
                return "Beauftragt";

            return "Offen";
        }

        return numeric switch
        {
            300 => "Abgelehnt",
            500 or 750 or 1000 => "Beauftragt",
            _ => "Offen"
        };
    }

    private static double DetermineOfferProbability(string rawStatus)
    {
        if (!int.TryParse(rawStatus, out var numeric))
            return 50;

        return numeric switch
        {
            100 => 35,
            200 => 65,
            300 => 0,
            500 or 750 or 1000 => 100,
            _ => 50
        };
    }

    private static bool IsOverdue(string dueDate) =>
        DateTime.TryParse(dueDate, out var due) && due.Date < DateTime.Today;

    private static string JoinNonEmpty(params string[] values) =>
        string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string HtmlToPlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = HtmlBreakRegex.Replace(text, "\n");
        text = HtmlBlockEndRegex.Replace(text, "\n");
        text = HtmlListItemRegex.Replace(text, "• ");
        text = HtmlTagRegex.Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');

        var lines = text
            .Split('\n')
            .Select(line => HorizontalWhitespaceRegex.Replace(line, " ").Trim())
            .ToList();

        var normalized = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Length == 0 && (normalized.Count == 0 || normalized[^1].Length == 0))
                continue;
            normalized.Add(line);
        }

        while (normalized.Count > 0 && normalized[^1].Length == 0)
            normalized.RemoveAt(normalized.Count - 1);

        return string.Join(Environment.NewLine, normalized);
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ContactAddressData ParseContactAddress(JsonElement item)
    {
        return new ContactAddressData
        {
            ContactId = GetNestedObjectId(item, "contact"),
            Street = GetString(item, "street"),
            ZipCode = GetString(item, "zip"),
            City = GetString(item, "city"),
            Country = GetNestedString(item, "country", "name", "nameEn", "code", "isoCode"),
            Name = GetString(item, "name"),
            Name2 = GetString(item, "name2")
        };
    }

    private static CommunicationWayData ParseCommunicationWay(JsonElement item)
    {
        return new CommunicationWayData
        {
            ContactId = GetNestedObjectId(item, "contact"),
            Type = GetString(item, "type"),
            Value = GetString(item, "value"),
            IsMain = IsTruthy(GetString(item, "main"))
        };
    }

    private static ContactAddressData? SelectPreferredAddress(IEnumerable<ContactAddressData> addresses)
    {
        return addresses
            .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.Street))
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.City))
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.ZipCode))
            .FirstOrDefault();
    }

    private static string FirstCommunicationValue(IReadOnlyCollection<CommunicationWayData>? communicationWays, params string[] types)
    {
        if (communicationWays == null || communicationWays.Count == 0)
            return "";

        return communicationWays
            .Where(x => types.Contains(x.Type, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Value))
            .OrderByDescending(x => x.IsMain)
            .Select(x => x.Value)
            .FirstOrDefault() ?? "";
    }

    private static bool IsTruthy(string value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private sealed class ContactAddressData
    {
        public string ContactId { get; init; } = "";
        public string Street { get; init; } = "";
        public string ZipCode { get; init; } = "";
        public string City { get; init; } = "";
        public string Country { get; init; } = "";
        public string Name { get; init; } = "";
        public string Name2 { get; init; } = "";
    }

    private sealed class CommunicationWayData
    {
        public string ContactId { get; init; } = "";
        public string Type { get; init; } = "";
        public string Value { get; init; } = "";
        public bool IsMain { get; init; }
    }
}
