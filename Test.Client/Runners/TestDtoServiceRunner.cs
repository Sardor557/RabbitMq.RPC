using AsbtCore.Broker.Demo.Contracts;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Client.Runners;

internal sealed class TestDtoServiceRunner
{
    // Approximate item counts calibrated against ~580 bytes / item:
    //   10 KB  => 17  items
    //  500 KB  => 870 items
    //    1 MB  => 1740 items
    private static readonly (string Label, int ItemCount)[] Scenarios =
    [
        ("10 KB  (~17 items)",   17),
        ("500 KB (~870 items)",  870),
        ("1 MB  (~1740 items)",  1740),
    ];

    private readonly ITestDtoService _service;
    private readonly ILogger<TestDtoServiceRunner> _logger;

    // Shared call counter — incremented from the hot loop, read by the timer.
    private int _callCount;

    public TestDtoServiceRunner(IHost host)
    {
        _service = host.Services.GetRequiredService<ITestDtoService>();
        _logger  = host.Services.GetRequiredService<ILogger<TestDtoServiceRunner>>();
    }

    /// <summary>
    /// Runs all three payload-size scenarios and prints a calls-per-second summary.
    /// </summary>
    public async Task RunAllScenariosAsync(CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.WriteLine("=== ITestDtoService benchmark (3 scenarios) ===");

        foreach (var (label, itemCount) in Scenarios)
        {
            ct.ThrowIfCancellationRequested();
            await RunScenarioAsync(label, itemCount, ct);
        }

        Console.WriteLine("=== Benchmark complete ===");
        Console.WriteLine();
    }

    // -------------------------------------------------------------------------

    private const int IterationsPerCallType = 10000;

    private async Task RunScenarioAsync(string label, int itemCount, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"--- Scenario: {label} ---");

        var payload = BuildPayload(itemCount);

        // --- Call type 1: EchoAsync ---
        await RunCallTypeAsync("[1] EchoAsync", ct,
            async () => { await _service.EchoAsync(payload); });

        // --- Call type 2: CountItemsAsync ---
        await RunCallTypeAsync("[2] CountItemsAsync", ct,
            async () => { await _service.CountItemsAsync(payload); });

        // --- Call type 3: EchoAsync (mutated payload) ---
        payload.DocumentType = "INVOICE";
        payload.Status       = "PENDING";
        await RunCallTypeAsync("[3] EchoAsync (mutated)", ct,
            async () => { await _service.EchoAsync(payload); });
    }

    private const int DegreeOfParallelism = 5;

    private async Task RunCallTypeAsync(string callTypeName, CancellationToken ct, Func<Task> action)
    {
        Console.WriteLine();
        Console.WriteLine($"  {callTypeName} — {IterationsPerCallType} iterations  ({DegreeOfParallelism} threads)...");

        Interlocked.Exchange(ref _callCount, 0);

        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timerTask = RunCallsPerSecondTimerAsync(callTypeName, timerCts.Token);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Split iterations evenly across DegreeOfParallelism worker tasks.
        var workerTasks = Enumerable.Range(0, DegreeOfParallelism).Select(async _ =>
        {
            for (int i = 0; i < IterationsPerCallType / DegreeOfParallelism; i++)
            {
                ct.ThrowIfCancellationRequested();
                await action();
                Interlocked.Increment(ref _callCount);
            }
        });

        await Task.WhenAll(workerTasks);

        sw.Stop();

        await timerCts.CancelAsync();
        try { await timerTask; } catch (OperationCanceledException) { /* expected */ }

        double callsPerSec = IterationsPerCallType / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"  {callTypeName} => {IterationsPerCallType} calls in {sw.ElapsedMilliseconds} ms  |  avg {callsPerSec:F1} calls/sec");
    }

    private async Task RunCallsPerSecondTimerAsync(string label, CancellationToken ct)
    {
        var lastCount = 0;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1_000, ct); }
            catch (OperationCanceledException) { break; }

            var current = Volatile.Read(ref _callCount);
            var delta   = current - lastCount;
            lastCount   = current;
            if (delta > 0)
                _logger.LogInformation("  {Label}  {Delta} calls/sec (running)", label, delta);
        }
    }

    // -------------------------------------------------------------------------

    private static TestDto BuildPayload(int itemCount)
    {
        var now = DateTime.UtcNow;
        var dto = new TestDto
        {
            RequestId       = Guid.NewGuid(),
            DocumentNumber  = $"DOC-{now:yyyyMMddHHmmss}",
            DocumentType    = "ORDER",
            Status          = "NEW",
            Priority        = "NORMAL",
            Source          = "Client",
            Channel         = "API",
            Region          = "UZ",
            Branch          = "HQ",
            Department      = "Sales",
            Division        = "Retail",
            CostCenter      = "CC-001",
            ProjectCode     = "PRJ-BENCH",
            ContractNumber  = "CTR-0001",
            ReferenceNumber = "REF-0001",
            ExternalId      = Guid.NewGuid().ToString(),
            CustomerId      = 1,
            CustomerCode    = "CUST-001",
            CustomerName    = "Test Customer",
            CustomerEmail   = "test@example.com",
            CustomerPhone   = "+998-90-000-00-00",
            CustomerTaxId   = "TAX-000001",
            CustomerAddress = "123 Test Street",
            CustomerCity    = "Tashkent",
            CustomerCountry = "UZ",
            CustomerPostalCode = "100000",
            CustomerSegment    = "B2B",
            CustomerCategory   = "Premium",
            SubTotal        = itemCount * 100m,
            DiscountTotal   = itemCount * 5m,
            TaxTotal        = itemCount * 12m,
            ShippingCost    = 15m,
            HandlingFee     = 5m,
            GrandTotal      = itemCount * 107m + 20m,
            PaidAmount      = 0m,
            BalanceDue      = itemCount * 107m + 20m,
            CurrencyCode    = "UZS",
            ExchangeRate    = 12600m,
            PaymentTerms    = "NET30",
            PaymentMethod   = "BANK_TRANSFER",
            ShipToName      = "Test Recipient",
            ShipToAddress   = "456 Delivery Road",
            ShipToCity      = "Tashkent",
            ShipToCountry   = "UZ",
            ShipToPostalCode = "100001",
            ShippingMethod  = "STANDARD",
            TrackingNumber  = string.Empty,
            Carrier         = "DHL",
            CreatedBy       = "bench-client",
            UpdatedBy       = "bench-client",
            ApprovedBy      = string.Empty,
            CreatedAt       = now,
            UpdatedAt       = now,
            DueDate         = now.AddDays(30),
            ShipDate        = now.AddDays(3),
            Tags            = "benchmark,test",
            Description     = "Benchmark payload",
            InternalNotes   = "Auto-generated",
            ExternalNotes   = string.Empty,
            LineItems       = BuildLineItems(itemCount, now),
        };

        return dto;
    }

    private static List<LineItemDto> BuildLineItems(int count, DateTime now) =>
        Enumerable.Range(1, count).Select(i => new LineItemDto
        {
            LineId          = i,
            ProductCode     = $"PROD-{i:D4}",
            ProductName     = $"Product {i}",
            Category        = "Electronics",
            SubCategory     = "Accessories",
            Brand           = "BrandX",
            Sku             = $"SKU-{i:D6}",
            Barcode         = $"8800000{i:D6}",
            UnitPrice       = 100m + i,
            DiscountPercent = 5m,
            DiscountAmount  = (100m + i) * 0.05m,
            TaxRate         = 12m,
            TaxAmount       = (100m + i) * 0.12m,
            NetPrice        = (100m + i) * 0.95m,
            TotalAmount     = (100m + i) * 0.95m * 1.12m,
            Quantity        = 1,
            Unit            = "PCS",
            WarehouseCode   = "WH-01",
            WarehouseName   = "Main Warehouse",
            SupplierCode    = $"SUP-{i % 10 + 1:D3}",
            SupplierName    = $"Supplier {i % 10 + 1}",
            CountryOfOrigin = "CN",
            CurrencyCode    = "UZS",
            Notes           = string.Empty,
            IsActive        = true,
            CreatedAt       = now,
            UpdatedAt       = now,
        }).ToList();
}
