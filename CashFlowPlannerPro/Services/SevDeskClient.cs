using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Services;

public static class SevDeskClient
{
    private const string BaseUrl = "https://my.sevdesk.de/api/v1/";
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
        var addressesTask = TryFetchObjectsAsync(apiToken, "ContactAddress?embed=country", cancellationToken);
        var communicationWaysTask = TryFetchObjectsAsync(apiToken, "CommunicationWay", cancellationToken);

        await Task.WhenAll(contactsTask, invoicesTask, ordersTask, addressesTask, communicationWaysTask);

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
            Invoices = invoicesTask.Result.Select(ParseInvoice).Where(i => i.Amount > 0).ToList(),
            Offers = ordersTask.Result
                .Select(ParseOffer)
                .Where(o => o != null)
                .Cast<SevDeskOfferPreview>()
                .Where(o => o.Amount > 0)
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

    private static SevDeskInvoicePreview ParseInvoice(JsonElement item)
    {
        var contact = GetNestedObject(item, "contact");
        var customerName = contact.HasValue
            ? ParseContact(contact.Value).DisplayName
            : GetString(item, "addressName", "name");

        var invoiceNumber = GetString(item, "invoiceNumber", "number");
        var description = JoinNonEmpty(invoiceNumber, GetString(item, "header", "text"));
        var issueDate = NormalizeDate(GetString(item, "invoiceDate", "create"));
        var dueDate = NormalizeDate(GetString(item, "dueDate"));
        var invoiceType = GetString(item, "invoiceType");
        var sourceStatus = BuildInvoiceStatusLabel(item, dueDate, invoiceType);

        return new SevDeskInvoicePreview
        {
            ExternalId = GetString(item, "id"),
            InvoiceNumber = invoiceNumber,
            CustomerName = customerName,
            IssueDate = issueDate,
            DueDate = dueDate,
            Amount = GetDouble(item, "sumGross", "totalGross", "invoiceSum"),
            Description = description,
            SourceStatus = sourceStatus,
            InvoiceType = invoiceType,
            Status = MapInvoiceStatus(sourceStatus)
        };
    }

    private static SevDeskOfferPreview? ParseOffer(JsonElement item)
    {
        var orderType = GetString(item, "orderType");
        if (!string.Equals(orderType, "AN", StringComparison.OrdinalIgnoreCase))
            return null;

        var contact = GetNestedObject(item, "contact");
        var customerName = contact.HasValue
            ? ParseContact(contact.Value).DisplayName
            : GetString(item, "addressName", "name");

        var offerNumber = GetString(item, "orderNumber", "number");
        var offerDate = NormalizeDate(GetString(item, "orderDate", "create"));
        var expectedDate = NormalizeDate(GetString(item, "deliveryDate", "sendDate"));
        if (string.IsNullOrWhiteSpace(expectedDate))
            expectedDate = offerDate;

        var rawStatus = GetString(item, "status");
        var sourceStatus = BuildOrderStatusLabel(rawStatus);
        var description = JoinNonEmpty(
            offerNumber,
            GetString(item, "header"),
            StripHtml(GetString(item, "headText")),
            StripHtml(GetString(item, "customerInternalNote")));

        return new SevDeskOfferPreview
        {
            ExternalId = GetString(item, "id"),
            OfferNumber = offerNumber,
            CustomerName = customerName,
            OfferDate = offerDate,
            DateExpected = expectedDate,
            Amount = GetDouble(item, "sumGross", "totalGross"),
            Probability = DetermineOfferProbability(rawStatus),
            Description = description,
            SourceStatus = sourceStatus,
            OrderType = orderType,
            PaymentDelay = GetInt(item, "paymentTerms") ?? 30,
            Status = MapOfferStatus(rawStatus)
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

    private static double GetDouble(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!item.TryGetProperty(propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var direct))
                return direct;

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return 0;
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

    private static string NormalizeDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return parsed.ToString("yyyy-MM-dd");

        return value.Length >= 10 ? value[..10] : value;
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

    private static string StripHtml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var withoutTags = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", " ");
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }

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
