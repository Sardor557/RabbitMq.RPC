using AsbtCore.Broker.Demo.Contracts;
using Contracts;

namespace Server.Services
{
    public sealed class TestDtoService : ITestDtoService
    {
        public Task<TestDto> EchoAsync(TestDto payload) => Task.FromResult(payload);

        public Task<int> CountItemsAsync(TestDto payload) =>
            Task.FromResult(payload.LineItems?.Count ?? 0);

        public Task<TestDto> GetItemsAsync(int cnt)
        {
            var now = DateTime.UtcNow;

            var lineItems = Enumerable.Range(1, cnt).Select(i => new LineItemDto
            {
                LineId = i,
                ProductCode = $"PROD-{i:D4}",
                ProductName = $"Product {i}",
                Category = "Category A",
                SubCategory = "Sub B",
                Brand = "BrandX",
                Sku = $"SKU-{i:D6}",
                Barcode = $"BAR{i:D10}",
                UnitPrice = 100m + i,
                DiscountPercent = 5m,
                DiscountAmount = (100m + i) * 0.05m,
                TaxRate = 12m,
                TaxAmount = (100m + i) * 0.12m,
                NetPrice = (100m + i) * 0.95m,
                TotalAmount = (100m + i) * 0.95m * i,
                Quantity = i,
                Unit = "pcs",
                WarehouseCode = "WH-01",
                WarehouseName = "Main Warehouse",
                SupplierCode = "SUP-001",
                SupplierName = "Supplier One",
                CountryOfOrigin = "UZ",
                CurrencyCode = "USD",
                Notes = $"Line note {i}",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            }).ToList();

            var dto = new TestDto
            {
                RequestId = Guid.NewGuid(),
                DocumentNumber = $"DOC-{now:yyyyMMdd}-0001",
                DocumentType = "Invoice",
                Status = "New",
                Priority = "Normal",
                Source = "API",
                Channel = "Online",
                Region = "Central",
                Branch = "HQ",
                Department = "Sales",
                Division = "Retail",
                CostCenter = "CC-100",
                ProjectCode = "PRJ-001",
                ContractNumber = "CNT-2025-001",
                ReferenceNumber = "REF-001",
                ExternalId = $"EXT-{Guid.NewGuid():N}",
                CustomerId = 1,
                CustomerCode = "CUST-001",
                CustomerName = "Test Customer",
                CustomerEmail = "customer@example.com",
                CustomerPhone = "+998901234567",
                CustomerTaxId = "TAX-123456",
                CustomerAddress = "123 Main St",
                CustomerCity = "Tashkent",
                CustomerCountry = "UZ",
                CustomerPostalCode = "100000",
                CustomerSegment = "B2B",
                CustomerCategory = "Premium",
                SubTotal = lineItems.Sum(l => l.TotalAmount),
                DiscountTotal = lineItems.Sum(l => l.DiscountAmount),
                TaxTotal = lineItems.Sum(l => l.TaxAmount),
                ShippingCost = 15m,
                HandlingFee = 5m,
                GrandTotal = lineItems.Sum(l => l.TotalAmount) + 15m + 5m,
                PaidAmount = 0m,
                BalanceDue = lineItems.Sum(l => l.TotalAmount) + 15m + 5m,
                CurrencyCode = "USD",
                ExchangeRate = 1m,
                PaymentTerms = "Net 30",
                PaymentMethod = "Bank Transfer",
                ShipToName = "Test Customer",
                ShipToAddress = "456 Delivery Ave",
                ShipToCity = "Tashkent",
                ShipToCountry = "UZ",
                ShipToPostalCode = "100000",
                ShippingMethod = "Standard",
                TrackingNumber = string.Empty,
                Carrier = "DHL",
                CreatedBy = "system",
                UpdatedBy = "system",
                ApprovedBy = string.Empty,
                CreatedAt = now,
                UpdatedAt = now,
                DueDate = now.AddDays(30),
                ShipDate = now.AddDays(3),
                Tags = "test,generated",
                Description = "Auto-generated test document",
                InternalNotes = "Internal test note",
                ExternalNotes = "External test note",
                LineItems = lineItems,
            };

            return Task.FromResult(dto);
        }
    }
}
