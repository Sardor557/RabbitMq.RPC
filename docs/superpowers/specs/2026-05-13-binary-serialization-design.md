# Binary Serialization Migration — Design Spec

**Date:** 2026-05-13
**Target version:** RabbitRpc 4.0.0 (breaking change from 3.1.x)
**Status:** Approved for implementation

## 1. Goal

Заменить System.Text.Json + `JsonElement` на бинарный формат (XPacketRpc как первый adapter) **с возможностью** будущей подмены на любой другой бинарный сериализатор. Single-format deployment — wire-совместимости с v3.1 нет.

## 2. Motivation

Текущая модель содержит две связанные проблемы:

1. `RpcArgument.Payload : JsonElement` и `RpcResponse.Result : JsonElement?` жёстко прибиты к `System.Text.Json`. Любой бинарный сериализатор вынужден делать round-trip через JSON или сосуществовать с JsonElement в DTO.
2. Сериализатор `IRpcSerializer` в Core импортирован из `System.Text.Json` де-факто, хотя интерфейс декларирован форматно-нейтральным.

Целевой формат — XPacketRpc (source-generated, .NET 10, length-prefixed varint для `byte[]`). XPacketRpc **не поддерживает** `List<object>`, type-id, динамическую десериализацию — это исключает «монолитную» сериализацию всего `RpcRequest` целиком. Аргументы обязаны быть сериализованы поштучно с известным типом из `MethodInfo`.

## 3. Architecture

### Пакеты после миграции

```
AsbtCore.Broker.Core           — format-agnostic, без System.Text.Json
AsbtCore.Broker.RabbitMq       — без изменений
AsbtCore.Broker.Client         — без System.Text.Json
AsbtCore.Broker.Server         — без System.Text.Json

[NEW] AsbtCore.Broker.Serialization.SystemTextJson
  Package: RabbitRpc.Serialization.SystemTextJson v1.0.0

[NEW] AsbtCore.Broker.Serialization.XPacketRpc
  Package: RabbitRpc.Serialization.XPacketRpc v1.0.0
```

### Граф зависимостей

```
RabbitRpc.Client ─┐
                  ├─► (Core + RabbitMq бандлятся через IncludeReferencedProjectsInPackage)
RabbitRpc.Server ─┘

RabbitRpc.Serialization.{SystemTextJson | XPacketRpc} ─► RabbitRpc.Client OR RabbitRpc.Server
```

Пользователь добавляет **два** NuGet — один shipping (Client/Server) и один adapter. Без adapter-а DI падает с явным сообщением на старте.

### Использование

```csharp
// Server
services.AddRabbitRpcServer(builder.Configuration)
        .UseXPacketRpcSerialization()
        .Register<ICalculatorService, CalculatorService>();

// Client
services.AddRabbitRpcClient(builder.Configuration)
        .UseXPacketRpcSerialization()
        .AddRpcProxy<ICalculatorService>();
```

## 4. Contracts

### `IRpcSerializer` (Core/Abstractions/)

```csharp
public interface IRpcSerializer
{
    /// Wire identifier — пишется в BasicProperties.ContentType при публикации.
    string ContentType { get; }

    /// Envelope-level — целый RpcRequest / RpcResponse → тело сообщения.
    ReadOnlyMemory<byte> Serialize<T>(T value);
    T? Deserialize<T>(ReadOnlyMemory<byte> payload);

    /// Fragment-level — один типизированный аргумент или результат.
    ReadOnlyMemory<byte> SerializeFragment(object? value, Type type);
    object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type);
}
```

### `IRpcSerializerInterfaceWarmup` (Core/Abstractions/) — **public**

Опциональный контракт, реализуется adapter-ом, если ему нужна warm-up-регистрация типов из сигнатур RPC-интерфейсов.

```csharp
public interface IRpcSerializerInterfaceWarmup
{
    void Prewarm(Type interfaceType);
}
```

`AddRpcProxy<T>()` / `Register<T,TImpl>()` дёргают `Prewarm(typeof(T))` на старте, если `IRpcSerializer is IRpcSerializerInterfaceWarmup`.

### `RpcContracts.cs` (Core)

```csharp
public sealed class RpcRequest
{
    public string RequestId { get; set; } = default!;
    public string InterfaceName { get; set; } = default!;
    public string MethodName { get; set; } = default!;
    public List<RpcArgument> Arguments { get; set; } = new();
}

public sealed class RpcArgument
{
    public string TypeName { get; set; } = default!;
    public ReadOnlyMemory<byte> Payload { get; set; }
}

public sealed class RpcResponse
{
    public string RequestId { get; set; } = default!;
    public bool Success { get; set; }
    public string? ResultTypeName { get; set; }
    public ReadOnlyMemory<byte>? Result { get; set; }
    public RpcError? Error { get; set; }
}

public sealed class RpcError
{
    public string Code { get; set; } = default!;
    public string Message { get; set; } = default!;
    public string? Details { get; set; }
    public string? ExceptionType { get; set; }
}
// using System.Text.Json — удалён.
// public static class RpcJson — удалён, переехал в SystemTextJson adapter.
```

### Удаления из Core

| Файл | Куда |
|---|---|
| `Serialization/JsonRpcSerializer.cs` | → SystemTextJson adapter |
| `Serialization/RpcSerializationHelper.cs` | удалить, логика переезжает в adapter |
| `Serialization/RpcSerializationServiceCollectionExtensions.cs` | удалить; заменяется `.UseXxxSerialization()` |
| `Serialization/IRpcSerializer.cs` | → `Core/Abstractions/IRpcSerializer.cs` (расширенный) |

`Serialization/StableTypeName.cs` **остаётся** internal (используется RpcClient + Dispatcher для проставления `TypeName` в `RpcArgument`).

### `RpcClient.cs` / `RpcRequestDispatcher.cs`

Оба получают `IRpcSerializer` через ctor. Замены:

```csharp
// RpcClient.BuildRequest
request.Arguments.Add(new RpcArgument {
    TypeName = StableTypeName.From(parameterType),
    Payload  = serializer.SerializeFragment(args[i], parameterType)
});

// RpcClient.SendAsync
return response.Result is null
    ? default
    : (T?)serializer.DeserializeFragment(response.Result.Value, typeof(T));

// RpcRequestDispatcher
args[i] = serializer.DeserializeFragment(arg.Payload, resolvedType);
// ...
return new RpcResponse {
    ...
    Result = logicalResultType is null ? null
                                       : serializer.SerializeFragment(result, logicalResultType)
};
```

Обработка ошибок (`type_not_found` / `deserialization_error` / `invocation_error`) — без изменений.

### Startup validation

```csharp
internal sealed class RpcSerializerStartupValidator : IValidateOptions<RpcOptions>
{
    private readonly IServiceProvider services;
    public ValidateOptionsResult Validate(string? name, RpcOptions options)
        => services.GetService<IRpcSerializer>() is not null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "No IRpcSerializer is registered. Call .UseXPacketRpcSerialization() " +
                "or .UseJsonRpcSerialization() on the builder, or register your own IRpcSerializer.");
}
```

Регистрируется через `ValidateOnStart` — fail на host-startup, а не при первом RPC-вызове.

## 5. XPacketRpc Adapter

### `XPacketRpcSerializer`

```csharp
public sealed class XPacketRpcSerializer : IRpcSerializer, IRpcSerializerInterfaceWarmup
{
    public string ContentType => "application/x-xpacket-rpc";

    public ReadOnlyMemory<byte> Serialize<T>(T value)
    {
        RpcTypeRegistry.EnsureRegistered(typeof(T));
        var writer = new ArrayBufferWriter<byte>();
        XPRpc.Write<T>(value, writer);
        return writer.WrittenMemory;
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        RpcTypeRegistry.EnsureRegistered(typeof(T));
        return XPRpc.Read<T>(payload.Span);
    }

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
        => FragmentInvokerCache.GetWriter(type)(value);

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
        => FragmentInvokerCache.GetReader(type)(payload);

    public void Prewarm(Type interfaceType)
        => RpcTypeRegistry.RegisterInterfaceSignatures(interfaceType);
}
```

### `FragmentInvokerCache`

`SerializeFragment(object? value, Type type)` принимает `Type` динамически — XPRpc.Write — дженерик. Прямой `MethodInfo.Invoke` = аллокация + бокс + ×10 overhead на горячем пути. Решение — кеш делегатов:

```csharp
internal static class FragmentInvokerCache
{
    private static readonly ConcurrentDictionary<Type, Func<object?, ReadOnlyMemory<byte>>> writers = new();
    private static readonly ConcurrentDictionary<Type, Func<ReadOnlyMemory<byte>, object?>> readers = new();

    public static Func<object?, ReadOnlyMemory<byte>> GetWriter(Type t) => writers.GetOrAdd(t, BuildWriter);
    public static Func<ReadOnlyMemory<byte>, object?> GetReader(Type t) => readers.GetOrAdd(t, BuildReader);

    private static Func<object?, ReadOnlyMemory<byte>> BuildWriter(Type t)
    {
        RpcTypeRegistry.EnsureRegistered(t);
        var closed = typeof(FragmentInvokerCache)
            .GetMethod(nameof(WriteTyped), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(t);
        return closed.CreateDelegate<Func<object?, ReadOnlyMemory<byte>>>();
    }

    private static ReadOnlyMemory<byte> WriteTyped<T>(object? value)
    {
        var buf = new ArrayBufferWriter<byte>();
        XPRpc.Write<T>((T)value!, buf);
        return buf.WrittenMemory;
    }

    private static Func<ReadOnlyMemory<byte>, object?> BuildReader(Type t)
    {
        RpcTypeRegistry.EnsureRegistered(t);
        var closed = typeof(FragmentInvokerCache)
            .GetMethod(nameof(ReadTyped), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(t);
        return closed.CreateDelegate<Func<ReadOnlyMemory<byte>, object?>>();
    }

    private static object? ReadTyped<T>(ReadOnlyMemory<byte> payload)
        => XPRpc.Read<T>(payload.Span);
}
```

Цена: первый вызов на новый тип — `MakeGenericMethod` (~µs); далее — `Func<,>` инвокация (~ns). `ConcurrentDictionary.GetOrAdd` гарантирует один билд на тип под конкурентным доступом.

### `RpcTypeRegistry`

Lazy-рекурсивная регистрация типов через `XPRpc.Touch<T>()` (тоже через делегаты, чтобы не платить за рефлексию повторно). При первом обращении к типу:

```csharp
internal static class RpcTypeRegistry
{
    private static readonly ConcurrentDictionary<Type, byte> seen = new();

    public static void EnsureRegistered(Type type)
    {
        if (!seen.TryAdd(type, 0)) return;
        if (IsPrimitiveOrBuiltin(type)) return;

        TouchCache.Get(type).Invoke();

        foreach (var nested in EnumerateNested(type))    // Nullable<T>, Task<T>, T[], List<T>, Dictionary<K,V>
            EnsureRegistered(nested);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            EnsureRegistered(prop.PropertyType);
    }

    public static void RegisterInterfaceSignatures(Type interfaceType)
    {
        foreach (var method in interfaceType.GetMethods())
        {
            foreach (var param in method.GetParameters())
                EnsureRegistered(param.ParameterType);

            var unwrapped = UnwrapTask(method.ReturnType);
            if (unwrapped is not null) EnsureRegistered(unwrapped);
        }
    }
}
```

Циклы по DTO-графу отсекаются `seen`-sentinel-ом. Примитивы / `string` / `enum` пропускаются.

### DI extensions

```csharp
public static class XPacketRpcServiceCollectionExtensions
{
    public static IRpcClientBuilder UseXPacketRpcSerialization(this IRpcClientBuilder b)
    {
        b.Services.TryAddSingleton<IRpcSerializer, XPacketRpcSerializer>();
        return b;
    }

    public static IRpcServerBuilder UseXPacketRpcSerialization(this IRpcServerBuilder b)
    {
        b.Services.TryAddSingleton<IRpcSerializer, XPacketRpcSerializer>();
        return b;
    }
}
```

## 6. SystemTextJson Adapter

Перенос существующего JSON-сериализатора в отдельный пакет, расширение до 4 методов, добавление `JsonConverter<ReadOnlyMemory<byte>>` для нового типа DTO.

```csharp
internal sealed class ReadOnlyMemoryByteJsonConverter : JsonConverter<ReadOnlyMemory<byte>>
{
    public override ReadOnlyMemory<byte> Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        => r.GetBytesFromBase64();
    public override void Write(Utf8JsonWriter w, ReadOnlyMemory<byte> v, JsonSerializerOptions o)
        => w.WriteBase64StringValue(v.Span);
}
```

Wire-формат для inner payload — base64-строка. Zero-copy на десериализации не получим (`base64-decode` создаёт новый массив) — это сознательный trade-off; JSON adapter остаётся для отладки / тестов / legacy. Документируется в README adapter-а.

`UseJsonRpcSerialization()` и `UseJsonRpcSerialization(Action<JsonSerializerOptions>)` extensions — две перегрузки.

## 7. Breaking Changes (v3.1 → v4.0)

1. **Wire-format несовместим.** v3.1 клиенты не разговаривают с v4 серверами и наоборот. Деплой — одновременная замена; рекомендуется слить очереди `rpc.*` до нуля либо запланировать окно даунтайма.
2. **Новый пакет обязателен**: `RabbitRpc.Serialization.XPacketRpc` ИЛИ `RabbitRpc.Serialization.SystemTextJson`.
3. **Новый DI-вызов обязателен**: `.UseXPacketRpcSerialization()` / `.UseJsonRpcSerialization()`. Без него — `OptionsValidationException` на старте.
4. **Удалено из Core**: `JsonRpcSerializer`, `RpcJson`, `RpcSerializationHelper`, `AddRpcSerialization()`. Переехало в SystemTextJson adapter.
5. **`RpcRequest.Arguments[i].Payload`**: `JsonElement` → `ReadOnlyMemory<byte>`. Касается только пользователей, строящих `RpcRequest` вручную (custom transport).
6. **`RpcResponse.Result`**: `JsonElement?` → `ReadOnlyMemory<byte>?`.
7. **`IRpcSerializer`** расширился до 4 методов. Кастомные реализации необходимо обновить.

## 8. Testing Strategy

### `AsbtCore.Broker.Serialization.SystemTextJson.Tests` (new)
- Envelope round-trip полный.
- Fragment round-trip: примитивы, `Guid`, `DateTime`/`DateTimeOffset`, `decimal`, enum, `List<DTO>`, `Dictionary<K,V>`, `Nullable<T>`, null reference, вложенные DTO.
- `ReadOnlyMemoryByteJsonConverter`: known-vector base64 ↔ bytes.
- Corrupted JSON → `JsonException`.
- `UseJsonRpcSerialization(configure)` применяет конфигурацию.

### `AsbtCore.Broker.Serialization.XPacketRpc.Tests` (new)
- Envelope + fragment round-trip аналогично.
- `FragmentInvokerCache`: concurrent `GetWriter` на 100 потоков → один билд (инструментация в тесте).
- `RpcTypeRegistry`: рекурсия по properties; self-reference DTO не приводит к stack overflow; `Nullable<T>` / `List<T>` / `T[]` развернуты.
- `Prewarm(typeof(IFoo))` регистрирует все типы параметров и возвращаемых типов.
- **Lifetime-тест**: сериализовать в буфер A → десериализовать с `A.AsMemory()` → перезаписать A нулями → `result.Arguments[i].Payload.Span` остаётся валидным (контракт adapter-а на изоляцию).

### `AsbtCore.Broker.Core.Tests` (updates)
- Удалить: `JsonRpcSerializerTests`, `RpcSerializationHelperTests`, `RpcSerializationServiceCollectionExtensionsTests`.
- `RpcRequestSerializationTests`: сравнения через `payload.Span.SequenceEqual(...)`.
- `PoisonReplyTests`: corrupted fragment → `deserialization_error` → DLQ.

### `AsbtCore.Broker.ClientServer.Tests` (updates)
- `Fixtures/TestSerializer.cs` — fake реализующий 4 метода (детерминированный wire без зависимости на JSON/XPacketRpc).
- `RpcRequestDispatcherTests` / `RpcClientTests`: assert через verify вызовов `SerializeFragment`/`DeserializeFragment` с правильным `Type`.

## 9. Risks

| # | Риск | Mitigation |
|---|---|---|
| 1 | **Lifetime буфера от RabbitMQ.** `BasicDeliverEventArgs.Body` после возврата handler-а может быть переиспользован — если `Payload` slice этого буфера, после async-границы данные corrupted. | Outer-сериализатор обязан гарантировать персистентность fragment-памяти как минимум до завершения dispatch. Простейший путь — `Deserialize<RpcRequest>` копирует body в свой `byte[]`, fragments — slice на копию. Lifetime-тест в каждом adapter-е валидирует. |
| 2 | XPacketRpc source generator не видит DTO без `XPacketRpc.Generators` package reference. | `FragmentInvokerCache.BuildWriter/Reader` перехватывает первую ошибку и оборачивает в `RpcSerializationException` с инструкцией добавить generator-пакет. |
| 3 | Полиморфизм DTO (`List<Animal>` с `Dog`) — не поддерживается XPacketRpc. | Документация. Совпадает с текущим System.Text.Json default. |
| 4 | NativeAOT несовместимость (`MakeGenericMethod`, `DispatchProxy`). | NativeAOT и так не поддерживается. Декларируется как not-supported. |
| 5 | Кастомные `IRpcSerializer` пользователей не компилируются. | Breaking change задокументирован в v4.0 migration. Default-методы не добавляем (скрывает breaking хуже, чем явный fail). |

## 10. Execution Strategy — параллельное распределение по агентам

```
Wave 1 (sequential):
  Agent A — Phase 1: Core contracts refactor (blocks all)

Wave 2 (parallel — 3 agents):
  Agent B — Phase 2: SystemTextJson adapter + tests
  Agent C — Phase 3: Client/Server integration + ClientServer.Tests update
  Agent D — Phase 4: XPacketRpc adapter + tests

Wave 3 (parallel — 2 agents):
  Agent E — Phase 5: sln + coverage + demo apps + README + version bump
  Agent F — Phase 6: benchmarks update + SerializerComparisonBench

Wave 4 (sequential):
  Agent G — integration: build all + run all tests + coverage check + smoke bench
```

### Контракты между Wave 2 агентами (полная изоляция)

- B не зависит от C/D. Свой проект, свои тесты.
- D не зависит от B/C. Свой проект, свои тесты.
- C использует `TestSerializer` fake — пишется внутри C, не из B/D.
- Все трое программируют против контракта, зафиксированного Agent A.

## 11. Locked Defaults (open questions resolved)

| # | Вопрос | Решение |
|---|---|---|
| 1 | Demo apps adapter | XPacketRpc (целевой формат) |
| 2 | XPacketRpc ContentType | `application/x-xpacket-rpc` |
| 3 | `IRpcSerializerInterfaceWarmup` visibility | **public** в Core |
| 4 | `StableTypeName` visibility | остаётся **internal** (YAGNI) |

## 12. Versioning

| Package | Version |
|---|---|
| `RabbitRpc.Client` | 4.0.0 |
| `RabbitRpc.Server` | 4.0.0 |
| `RabbitRpc.Serialization.SystemTextJson` | 1.0.0 (new) |
| `RabbitRpc.Serialization.XPacketRpc` | 1.0.0 (new) |

Adapter-пакеты версионируются независимо — они зависят только от `IRpcSerializer` контракта, стабильного внутри major.
