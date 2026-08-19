using System.Collections.ObjectModel;

namespace CashFlowPlannerPro.Models;

public sealed class DocumentContent
{
    public long Id { get; set; }
    public string Header { get; set; } = "";
    public string PreText { get; set; } = "";
    public string PostText { get; set; } = "";
    public string InternalNote { get; set; } = "";
    public string? SourceProvider { get; set; }
    public string? SourceEntityType { get; set; }
    public string? SourceExternalId { get; set; }
    public string? SourceSnapshotJson { get; set; }
    public string? LastImportedAt { get; set; }
    public ObservableCollection<DocumentLineItem> LineItems { get; set; } = [];

    public DocumentContent DeepClone()
    {
        return new DocumentContent
        {
            Id = Id,
            Header = Header,
            PreText = PreText,
            PostText = PostText,
            InternalNote = InternalNote,
            SourceProvider = SourceProvider,
            SourceEntityType = SourceEntityType,
            SourceExternalId = SourceExternalId,
            SourceSnapshotJson = SourceSnapshotJson,
            LastImportedAt = LastImportedAt,
            LineItems = new ObservableCollection<DocumentLineItem>(LineItems.Select(item => item.DeepClone()))
        };
    }
}

public sealed class DocumentLineItem
{
    public long Id { get; set; }
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

    public DocumentLineItem DeepClone()
    {
        return new DocumentLineItem
        {
            Id = Id,
            SourceItemId = SourceItemId,
            SortOrder = SortOrder,
            PositionNumber = PositionNumber,
            Name = Name,
            Description = Description,
            Quantity = Quantity,
            Unit = Unit,
            UnitPrice = UnitPrice,
            DiscountPercent = DiscountPercent,
            TaxRate = TaxRate,
            NetAmount = NetAmount,
            GrossAmount = GrossAmount,
            IsOptional = IsOptional
        };
    }
}
