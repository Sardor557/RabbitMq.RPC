using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Serialization.MemoryPack;
using AsbtCore.Broker.Server;
using BenchmarkDotNet.Attributes;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Benchmarks;

// ---------------------------------------------------------------------------
// Self-contained DTOs for this benchmark (no external contract dependencies)
// ---------------------------------------------------------------------------

[MemoryPackable]
public sealed partial class BenchLineItemDto
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
public sealed partial class BenchTestDto
{
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
    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal GrandTotal { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string ShipToName { get; set; } = string.Empty;
    public string ShipToAddress { get; set; } = string.Empty;
    public string ShipToCity { get; set; } = string.Empty;
    public string ShipToCountry { get; set; } = string.Empty;
    public string ShippingMethod { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Tags { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InternalNotes { get; set; } = string.Empty;
    public List<BenchLineItemDto> LineItems { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Service contract and implementation for in-process round-trip
// ---------------------------------------------------------------------------

public interface IBenchTestDtoService
{
    Task<BenchTestDto> EchoAsync(BenchTestDto payload);
}

public sealed class BenchTestDtoService : IBenchTestDtoService
{
    public Task<BenchTestDto> EchoAsync(BenchTestDto payload) => Task.FromResult(payload);
}

// ---------------------------------------------------------------------------
// Test data factory — produces pre-built payloads of ~10 KB / ~500 KB / ~1 MB
// ---------------------------------------------------------------------------

/// <summary>
/// Approximate JSON sizes achieved by varying the number of line items.
/// Each <see cref="BenchLineItemDto"/> serializes to roughly 550–600 bytes of JSON.
/// </summary>
public static class TestPayloadFactory
{
    // Approx item counts calibrated against ~580 bytes / item:
    //   10 KB  => 17  items
    //  500 KB  => 870 items
    //    1 MB  => 1740 items
    private const int Items10Kb = 17;
    private const int Items500Kb = 870;
    private const int Items1Mb = 1740;

    public static BenchTestDto Create10Kb() => Build(Items10Kb);
    public static BenchTestDto Create500Kb() => Build(Items500Kb);
    public static BenchTestDto Create1Mb() => Build(Items1Mb);

    private static BenchTestDto Build(int lineCount)
    {
        var now = DateTime.UtcNow;
        var dto = new BenchTestDto
        {
            RequestId = Guid.NewGuid(),
            DocumentNumber = "DOC-20240101-0001",
            DocumentType = "SalesOrder",
            Status = "Confirmed",
            Priority = "High",
            Source = "WebShop",
            Channel = "Online",
            Region = "Central-Asia",
            Branch = "Tashkent-01",
            Department = "Sales",
            Division = "B2B",
            CostCenter = "CC-001",
            ProjectCode = "PRJ-2024-BENCH",
            ContractNumber = "CTR-2024-0099",
            ReferenceNumber = "REF-BENCH-001",
            ExternalId = "EXT-9999",
            CustomerId = 42,
            CustomerCode = "CUST-0042",
            CustomerName = "AsbtCore Test Customer Ltd.",
            CustomerEmail = "customer@test.example.com",
            CustomerPhone = "+998901234567",
            CustomerTaxId = "123456789",
            CustomerAddress = "Amir Temur Ko'chasi 1",
            CustomerCity = "Tashkent",
            CustomerCountry = "Uzbekistan",
            CustomerPostalCode = "100000",
            CustomerSegment = "Enterprise",
            CustomerCategory = "Platinum",
            SubTotal = 99_900m,
            DiscountTotal = 999m,
            TaxTotal = 11_988m,
            ShippingCost = 50m,
            GrandTotal = 110_939m,
            CurrencyCode = "UZS",
            PaymentTerms = "Net30",
            PaymentMethod = "BankTransfer",
            ShipToName = "AsbtCore Warehouse",
            ShipToAddress = "Industrial Zone 12",
            ShipToCity = "Tashkent",
            ShipToCountry = "Uzbekistan",
            ShippingMethod = "Ground",
            TrackingNumber = "TRK-000000001",
            CreatedBy = "bench-setup",
            ApprovedBy = "bench-approver",
            CreatedAt = now,
            UpdatedAt = now,
            Tags = "benchmark,perf,load",
            Description = "Automatically generated payload for RPC payload-size benchmark.",
            InternalNotes = "No action required — benchmark data only.",
            LineItems = BuildLineItems(lineCount, now)
        };
        return dto;
    }

    private static List<BenchLineItemDto> BuildLineItems(int count, DateTime now)
    {
        var items = new List<BenchLineItemDto>(count);
        for (int i = 1; i <= count; i++)
        {
            items.Add(new BenchLineItemDto
            {
                LineId = i,
                ProductCode = $"PROD-{i:D6}",
                ProductName = $"Product Number {i} — Long Display Name For Bench",
                Category = "Electronics",
                SubCategory = "Accessories",
                Brand = "AsbtCore Brand",
                Sku = $"SKU-{i:D8}",
                Barcode = $"880000{i:D7}",
                UnitPrice = 100m + i,
                DiscountPercent = 1m,
                DiscountAmount = 1m + i * 0.01m,
                TaxRate = 12m,
                TaxAmount = (100m + i) * 0.12m,
                NetPrice = 99m + i,
                TotalAmount = (99m + i) * 1.12m,
                Quantity = (i % 10) + 1,
                Unit = "pcs",
                WarehouseCode = "WH-001",
                WarehouseName = "Main Warehouse Tashkent",
                SupplierCode = $"SUP-{(i % 5) + 1:D3}",
                SupplierName = $"Supplier {(i % 5) + 1} LLC",
                CountryOfOrigin = "Uzbekistan",
                CurrencyCode = "UZS",
                Notes = $"Bench line item {i}. No special handling.",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        return items;
    }
}

// ---------------------------------------------------------------------------
// Benchmark
// ---------------------------------------------------------------------------

/// <summary>
/// Measures RPC client round-trip latency and allocations for three payload sizes:
/// ~10 KB, ~500 KB, and ~1 MB — using an in-process <see cref="InMemoryTransport"/>.
/// </summary>
[InProcessJob]
[MemoryDiagnoser]
public class PayloadSizeBench
{
    private IBenchTestDtoService _proxy10Kb = null!;
    private IBenchTestDtoService _proxy500Kb = null!;
    private IBenchTestDtoService _proxy1Mb = null!;

    private BenchTestDto _payload10Kb = null!;
    private BenchTestDto _payload500Kb = null!;
    private BenchTestDto _payload1Mb = null!;

    [GlobalSetup]
    public void Setup()
    {
        _payload10Kb = TestPayloadFactory.Create10Kb();
        _payload500Kb = TestPayloadFactory.Create500Kb();
        _payload1Mb = TestPayloadFactory.Create1Mb();

        _proxy10Kb = BuildProxy();
        _proxy500Kb = BuildProxy();
        _proxy1Mb = BuildProxy();
    }

    /// <summary>Round-trip call with a ~10 KB payload.</summary>
    [Benchmark]
    public Task<BenchTestDto> Echo_10Kb() => _proxy10Kb.EchoAsync(_payload10Kb);

    /// <summary>Round-trip call with a ~500 KB payload.</summary>
    [Benchmark]
    public Task<BenchTestDto> Echo_500Kb() => _proxy500Kb.EchoAsync(_payload500Kb);

    /// <summary>Round-trip call with a ~1 MB payload.</summary>
    [Benchmark]
    public Task<BenchTestDto> Echo_1Mb() => _proxy1Mb.EchoAsync(_payload1Mb);

    private static IBenchTestDtoService BuildProxy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BenchTestDtoService>();
        services.AddSingleton<IBenchTestDtoService>(sp => sp.GetRequiredService<BenchTestDtoService>());
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new RpcOptions
        {
            HostName = "192.168.0.112",
            VirtualHost = "/",
            UserName = "mqadmin",
            Password = "@$bt$0ft",
            ClientProvidedName = "demo-rpc-server",
            Port = 5672,
            DefaultTimeoutSeconds = 30
        });

        IRpcSerializer serializer = new MemoryPackRpcSerializer();
        var resolver = new DefaultRpcRouteResolver(options);
        var registry = new RpcServerRegistry(
            new[] { new RpcServerRegistration(typeof(IBenchTestDtoService), typeof(BenchTestDtoService)) }, resolver);
        var dispatcher = new RpcRequestDispatcher(
            registry, sp.GetRequiredService<IServiceScopeFactory>(), serializer);
        var transport = new InMemoryTransport(dispatcher);
        var client = new RpcClient(transport, resolver, serializer, options);
        var factory = new RpcProxyFactory(client, serializer, options);
        return factory.CreateProxy<IBenchTestDtoService>();
    }
}
