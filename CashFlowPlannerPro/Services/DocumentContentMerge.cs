using System.Collections.ObjectModel;
using System.Text.Json;
using CashFlowPlannerPro.Models;

namespace CashFlowPlannerPro.Services;

/// <summary>
/// Performs a conservative three-way merge between the last imported source
/// snapshot, the locally edited document content, and the current source data.
/// </summary>
public static class DocumentContentMerge
{
    private const int SnapshotVersion = 1;
    private const double NumberTolerance = 0.000001;
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static bool HasExactSource(
        DocumentContent? content,
        string provider,
        string entityType,
        string externalId)
    {
        return content != null
            && !string.IsNullOrWhiteSpace(externalId)
            && string.Equals(content.SourceProvider, provider, StringComparison.Ordinal)
            && string.Equals(content.SourceEntityType, entityType, StringComparison.Ordinal)
            && string.Equals(content.SourceExternalId, externalId, StringComparison.Ordinal);
    }

    public static bool HasSourceIdentity(DocumentContent? content)
    {
        return content != null
            && (!string.IsNullOrWhiteSpace(content.SourceProvider)
                || !string.IsNullOrWhiteSpace(content.SourceEntityType)
                || !string.IsNullOrWhiteSpace(content.SourceExternalId));
    }

    /// <summary>
    /// Clones freshly parsed source content and records the clean source state
    /// used as the base for the next import. Database identities never enter the
    /// snapshot.
    /// </summary>
    public static DocumentContent PrepareImported(DocumentContent imported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ValidateCompleteSourceIdentity(imported);

        var prepared = imported.DeepClone();
        prepared.Id = 0;
        foreach (var item in prepared.LineItems)
            item.Id = 0;
        _ = CreateKeyedEntries(prepared.LineItems, "importierten");

        prepared.SourceSnapshotJson = SerializeSnapshot(prepared);
        prepared.LastImportedAt = DateTime.UtcNow.ToString("O");
        return prepared;
    }

    /// <summary>
    /// Merges current source data into local content. A local value is updated
    /// only when it still equals the last imported value. Locally changed values
    /// and positions are retained; new source positions are added; source
    /// positions deleted remotely are removed only when they were unchanged
    /// locally.
    /// </summary>
    public static DocumentContent MergeImported(DocumentContent? local, DocumentContent imported)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ValidateCompleteSourceIdentity(imported);

        local ??= new DocumentContent();
        if (HasSourceIdentity(local)
            && !HasExactSource(
                local,
                imported.SourceProvider!,
                imported.SourceEntityType!,
                imported.SourceExternalId!))
        {
            throw new InvalidOperationException(
                "Der lokale Dokumentinhalt ist bereits mit einer anderen Quelle verknüpft.");
        }

        var result = local.DeepClone();
        var baseline = TryDeserializeSnapshot(local.SourceSnapshotJson);
        bool hasBaseline = baseline != null;

        result.Header = MergeString(local.Header, baseline?.Header, imported.Header, hasBaseline);
        result.PreText = MergeString(local.PreText, baseline?.PreText, imported.PreText, hasBaseline);
        result.PostText = MergeString(local.PostText, baseline?.PostText, imported.PostText, hasBaseline);
        result.InternalNote = MergeString(local.InternalNote, baseline?.InternalNote, imported.InternalNote, hasBaseline);
        result.LineItems = MergeLineItems(
            local.LineItems ?? [],
            baseline?.LineItems?.Select(item => (DocumentLineItem)item) ?? [],
            imported.LineItems ?? [],
            hasBaseline);

        result.SourceProvider = imported.SourceProvider;
        result.SourceEntityType = imported.SourceEntityType;
        result.SourceExternalId = imported.SourceExternalId;
        result.SourceSnapshotJson = SerializeSnapshot(imported);
        result.LastImportedAt = DateTime.UtcNow.ToString("O");
        return result;
    }

    public static string SerializeSnapshot(DocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return JsonSerializer.Serialize(ToSnapshot(content), SnapshotJsonOptions);
    }

    private static ObservableCollection<DocumentLineItem> MergeLineItems(
        IEnumerable<DocumentLineItem> localItems,
        IEnumerable<DocumentLineItem> baselineItems,
        IEnumerable<DocumentLineItem> importedItems,
        bool hasBaseline)
    {
        var localEntries = CreateKeyedEntries(localItems, "lokalen");
        var baselineEntries = hasBaseline
            ? CreateKeyedEntries(baselineItems, "gespeicherten")
            : [];
        var importedEntries = CreateKeyedEntries(importedItems, "importierten");

        var baselineByKey = baselineEntries.ToDictionary(entry => entry.Key, entry => entry.Item, StringComparer.Ordinal);
        var importedByKey = importedEntries.ToDictionary(entry => entry.Key, entry => entry.Item, StringComparer.Ordinal);
        var consumedImportedKeys = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<(DocumentLineItem Item, int StableIndex)>();
        int stableIndex = 0;

        foreach (var localEntry in localEntries)
        {
            if (importedByKey.TryGetValue(localEntry.Key, out var importedItem))
            {
                consumedImportedKeys.Add(localEntry.Key);
                if (hasBaseline && baselineByKey.TryGetValue(localEntry.Key, out var baselineItem))
                {
                    merged.Add((MergeLineItem(localEntry.Item, baselineItem, importedItem), stableIndex++));
                }
                else
                {
                    // Without a known base, an existing local position may have
                    // been edited. Preserve it rather than guessing.
                    merged.Add((localEntry.Item.DeepClone(), stableIndex++));
                }

                continue;
            }

            if (!hasBaseline
                || !baselineByKey.TryGetValue(localEntry.Key, out var oldImportedItem)
                || !LineItemsEqual(localEntry.Item, oldImportedItem))
            {
                // Local-only position, or a remotely deleted position that has
                // since been changed locally.
                merged.Add((localEntry.Item.DeepClone(), stableIndex++));
            }
        }

        foreach (var importedEntry in importedEntries)
        {
            if (consumedImportedKeys.Contains(importedEntry.Key))
                continue;

            if (hasBaseline && baselineByKey.ContainsKey(importedEntry.Key))
            {
                // The source position existed at the last import but is absent
                // locally now: that absence is a local deletion and must not be
                // undone by a reimport.
                continue;
            }

            var added = importedEntry.Item.DeepClone();
            added.Id = 0;
            merged.Add((added, stableIndex++));
        }

        return new ObservableCollection<DocumentLineItem>(
            merged
                .OrderBy(entry => entry.Item.SortOrder)
                .ThenBy(entry => entry.StableIndex)
                .Select(entry => entry.Item));
    }

    private static DocumentLineItem MergeLineItem(
        DocumentLineItem local,
        DocumentLineItem baseline,
        DocumentLineItem imported)
    {
        var merged = local.DeepClone();
        merged.SourceItemId = imported.SourceItemId;
        merged.SortOrder = MergeValue(local.SortOrder, baseline.SortOrder, imported.SortOrder);
        merged.PositionNumber = MergeString(local.PositionNumber, baseline.PositionNumber, imported.PositionNumber, true);
        merged.Name = MergeString(local.Name, baseline.Name, imported.Name, true);
        merged.Description = MergeString(local.Description, baseline.Description, imported.Description, true);
        merged.Quantity = MergeNumber(local.Quantity, baseline.Quantity, imported.Quantity);
        merged.Unit = MergeString(local.Unit, baseline.Unit, imported.Unit, true);
        merged.UnitPrice = MergeNumber(local.UnitPrice, baseline.UnitPrice, imported.UnitPrice);
        merged.DiscountPercent = MergeNumber(local.DiscountPercent, baseline.DiscountPercent, imported.DiscountPercent);
        merged.TaxRate = MergeNumber(local.TaxRate, baseline.TaxRate, imported.TaxRate);
        merged.NetAmount = MergeNumber(local.NetAmount, baseline.NetAmount, imported.NetAmount);
        merged.GrossAmount = MergeNumber(local.GrossAmount, baseline.GrossAmount, imported.GrossAmount);
        merged.IsOptional = MergeValue(local.IsOptional, baseline.IsOptional, imported.IsOptional);
        return merged;
    }

    private static string MergeString(string? local, string? baseline, string? imported, bool hasBaseline)
    {
        local ??= "";
        imported ??= "";

        if (hasBaseline)
            return string.Equals(local, baseline ?? "", StringComparison.Ordinal) ? imported : local;

        return string.IsNullOrWhiteSpace(local) ? imported : local;
    }

    private static double MergeNumber(double local, double baseline, double imported) =>
        NumbersEqual(local, baseline) ? imported : local;

    private static T MergeValue<T>(T local, T baseline, T imported)
        where T : IEquatable<T> =>
        local.Equals(baseline) ? imported : local;

    private static bool LineItemsEqual(DocumentLineItem left, DocumentLineItem right)
    {
        return string.Equals(left.SourceItemId, right.SourceItemId, StringComparison.Ordinal)
            && left.SortOrder == right.SortOrder
            && string.Equals(left.PositionNumber, right.PositionNumber, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
            && NumbersEqual(left.Quantity, right.Quantity)
            && string.Equals(left.Unit, right.Unit, StringComparison.Ordinal)
            && NumbersEqual(left.UnitPrice, right.UnitPrice)
            && NumbersEqual(left.DiscountPercent, right.DiscountPercent)
            && NumbersEqual(left.TaxRate, right.TaxRate)
            && NumbersEqual(left.NetAmount, right.NetAmount)
            && NumbersEqual(left.GrossAmount, right.GrossAmount)
            && left.IsOptional == right.IsOptional;
    }

    private static bool NumbersEqual(double left, double right) =>
        Math.Abs(left - right) <= NumberTolerance;

    private static List<KeyedLineItem> CreateKeyedEntries(
        IEnumerable<DocumentLineItem> items,
        string sourceDescription)
    {
        var entries = new List<KeyedLineItem>();
        var anonymousOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var seenSourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item == null)
                continue;

            string key;
            if (!string.IsNullOrWhiteSpace(item.SourceItemId))
            {
                if (!seenSourceIds.Add(item.SourceItemId))
                {
                    throw new InvalidOperationException(
                        $"Die {sourceDescription} Dokumentpositionen enthalten die sevDesk-ID '{item.SourceItemId}' mehrfach.");
                }

                key = $"id:{item.SourceItemId}";
            }
            else
            {
                var baseKey = $"anonymous:{item.PositionNumber}\u001f{item.SortOrder}";
                anonymousOccurrences.TryGetValue(baseKey, out var occurrence);
                anonymousOccurrences[baseKey] = occurrence + 1;
                key = $"{baseKey}\u001f{occurrence}";
            }

            entries.Add(new KeyedLineItem(key, item));
        }

        return entries;
    }

    private static void ValidateCompleteSourceIdentity(DocumentContent content)
    {
        if (string.IsNullOrWhiteSpace(content.SourceProvider)
            || string.IsNullOrWhiteSpace(content.SourceEntityType)
            || string.IsNullOrWhiteSpace(content.SourceExternalId))
        {
            throw new ArgumentException(
                "Importierter Dokumentinhalt benötigt Anbieter, Entitätstyp und externe ID.",
                nameof(content));
        }
    }

    private static ContentSnapshot ToSnapshot(DocumentContent content)
    {
        return new ContentSnapshot
        {
            Version = SnapshotVersion,
            Header = content.Header ?? "",
            PreText = content.PreText ?? "",
            PostText = content.PostText ?? "",
            InternalNote = content.InternalNote ?? "",
            LineItems = (content.LineItems ?? [])
                .Where(item => item != null)
                .Select(item => new LineItemSnapshot
                {
                    SourceItemId = item.SourceItemId,
                    SortOrder = item.SortOrder,
                    PositionNumber = item.PositionNumber ?? "",
                    Name = item.Name ?? "",
                    Description = item.Description ?? "",
                    Quantity = item.Quantity,
                    Unit = item.Unit ?? "",
                    UnitPrice = item.UnitPrice,
                    DiscountPercent = item.DiscountPercent,
                    TaxRate = item.TaxRate,
                    NetAmount = item.NetAmount,
                    GrossAmount = item.GrossAmount,
                    IsOptional = item.IsOptional
                })
                .ToList()
        };
    }

    private static ContentSnapshot? TryDeserializeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var snapshot = JsonSerializer.Deserialize<ContentSnapshot>(json, SnapshotJsonOptions);
            return snapshot is { Version: SnapshotVersion } ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed record KeyedLineItem(string Key, DocumentLineItem Item);

    private sealed class ContentSnapshot
    {
        public int Version { get; set; }
        public string Header { get; set; } = "";
        public string PreText { get; set; } = "";
        public string PostText { get; set; } = "";
        public string InternalNote { get; set; } = "";
        public List<LineItemSnapshot> LineItems { get; set; } = [];
    }

    private sealed class LineItemSnapshot
    {
        public string? SourceItemId { get; set; }
        public int SortOrder { get; set; }
        public string PositionNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double Quantity { get; set; } = 1;
        public string Unit { get; set; } = "";
        public double UnitPrice { get; set; }
        public double DiscountPercent { get; set; }
        public double TaxRate { get; set; }
        public double NetAmount { get; set; }
        public double GrossAmount { get; set; }
        public bool IsOptional { get; set; }

        public static implicit operator DocumentLineItem(LineItemSnapshot snapshot)
        {
            return new DocumentLineItem
            {
                SourceItemId = snapshot.SourceItemId,
                SortOrder = snapshot.SortOrder,
                PositionNumber = snapshot.PositionNumber,
                Name = snapshot.Name,
                Description = snapshot.Description,
                Quantity = snapshot.Quantity,
                Unit = snapshot.Unit,
                UnitPrice = snapshot.UnitPrice,
                DiscountPercent = snapshot.DiscountPercent,
                TaxRate = snapshot.TaxRate,
                NetAmount = snapshot.NetAmount,
                GrossAmount = snapshot.GrossAmount,
                IsOptional = snapshot.IsOptional
            };
        }
    }
}
