using MemoryPack;

namespace AsbtCore.Broker.Demo.Contracts
{
    [MemoryPackable]
    public sealed partial class LineItemDto
    {
        public int LineId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal NetPrice { get; set; }
        public decimal TotalAmount { get; set; }
        public int Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string WarehouseCode { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string CountryOfOrigin { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    [MemoryPackable]
    public sealed partial class TestDto
    {
        // Header fields
        public Guid RequestId { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public string CostCenter { get; set; } = string.Empty;
        public string ProjectCode { get; set; } = string.Empty;
        public string ContractNumber { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;

        // Customer fields
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string CustomerTaxId { get; set; } = string.Empty;
        public string CustomerAddress { get; set; } = string.Empty;
        public string CustomerCity { get; set; } = string.Empty;
        public string CustomerCountry { get; set; } = string.Empty;
        public string CustomerPostalCode { get; set; } = string.Empty;
        public string CustomerSegment { get; set; } = string.Empty;
        public string CustomerCategory { get; set; } = string.Empty;

        // Financial fields
        public decimal SubTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal HandlingFee { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal BalanceDue { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; }
        public string PaymentTerms { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;

        // Shipping/delivery
        public string ShipToName { get; set; } = string.Empty;
        public string ShipToAddress { get; set; } = string.Empty;
        public string ShipToCity { get; set; } = string.Empty;
        public string ShipToCountry { get; set; } = string.Empty;
        public string ShipToPostalCode { get; set; } = string.Empty;
        public string ShippingMethod { get; set; } = string.Empty;
        public string TrackingNumber { get; set; } = string.Empty;
        public string Carrier { get; set; } = string.Empty;

        // Metadata
        public string CreatedBy { get; set; } = string.Empty;
        public string UpdatedBy { get; set; } = string.Empty;
        public string ApprovedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ShipDate { get; set; }
        public string Tags { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string InternalNotes { get; set; } = string.Empty;
        public string ExternalNotes { get; set; } = string.Empty;

        // Line items
        public List<LineItemDto> LineItems { get; set; } = new();
    }
}
