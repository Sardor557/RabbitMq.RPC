# AsbtCore.Broker — RabbitMQ RPC

[English version](README.md)

Легковесный RPC-фреймворк поверх RabbitMQ для .NET 10: типобезопасные контракты через C#-интерфейсы, DI-интеграция на клиенте и сервере, JSON-сериализация, паттерн reply-queue, publisher confirms, per-route dead-letter очереди.

Репозиторий публикует два потребительских NuGet-пакета: **`AsbtCore.Broker.Client`** и **`AsbtCore.Broker.Server`**. Остальные сборки (`Core`, `RabbitMq`) подтягиваются транзитивно.

---

## Установка

**Клиентское приложение** (вызывает удалённые сервисы):

```bash
dotnet add package AsbtCore.Broker.Client
```

**Серверное приложение** (хостит реализации):

```bash
dotnet add package AsbtCore.Broker.Server
```

**Общий проект контрактов** — обычная class library с интерфейсами и DTO, на неё ссылаются обе стороны. Ссылка на `AsbtCore.Broker.*` в ней не нужна.

---

## Структура пакетов

```mermaid
graph TD
    subgraph public["Потребительские NuGet-пакеты"]
        CLIENT["AsbtCore.Broker.Client"]
        SERVER["AsbtCore.Broker.Server"]
    end
    subgraph adapters["Адаптеры сериализации (выберите один)"]
        XPRPC["AsbtCore.Broker.Serialization.XPacketRpc\nБинарный, по умолчанию с v4.0"]
        STJ["AsbtCore.Broker.Serialization.SystemTextJson\nJSON, v3-совместимый формат"]
    end
    subgraph internal["Внутренние (подтягиваются транзитивно)"]
        CORE["AsbtCore.Broker.Core\nКонтракты · IRpcSerializer · Маршрутизация · Опции"]
        RMQ["AsbtCore.Broker.RabbitMq\nСоединение · Transport · TransportHost"]
    end

    CLIENT --> CORE
    CLIENT --> RMQ
    SERVER --> CORE
    SERVER --> RMQ
    RMQ    --> CORE
    XPRPC  --> CORE
    STJ    --> CORE

    style CLIENT fill:#4dabf7,color:#fff,stroke:#339af0
    style SERVER fill:#69db7c,color:#fff,stroke:#40c057
    style XPRPC  fill:#ffd43b,stroke:#f08c00
    style STJ    fill:#ffd43b,stroke:#f08c00
    style CORE   fill:#f8f9fa,stroke:#adb5bd
    style RMQ    fill:#f8f9fa,stroke:#adb5bd
```

| Пакет | Содержимое |
|---|---|
| `AsbtCore.Broker.Core` | `RpcRequest/Response`, `IRpcTransport`, `IRpcSerializer`, `IRpcRouteResolver`, `RpcOptions`, `RpcRemoteException`, `StableTypeName` |
| `AsbtCore.Broker.RabbitMq` | `RabbitMqRpcTransport` (клиент), `RabbitMqRpcTransportHost` (сервер), `IRabbitMqConnectionProvider` |
| `AsbtCore.Broker.Client` | `RpcClient`, `RpcProxyFactory` (`DispatchProxy`), DI: `AddRabbitRpcClient` (возвращает `RpcClientBuilder`) / `RpcClientBuilder.AddProxy<T>()` |
| `AsbtCore.Broker.Server` | `RpcServerBuilder`, `RpcServerRegistry`, `RpcRequestDispatcher`, `RpcServerHostedService`, DI: `AddRabbitRpcServer` |
| `AsbtCore.Broker.Serialization.XPacketRpc` | `XPacketRpcSerializer` (бинарный), DI-расширение `UseXPacketRpcSerialization()`. **По умолчанию с v4.0.** |
| `AsbtCore.Broker.Serialization.SystemTextJson` | `JsonRpcSerializer`, DI-расширение `UseJsonRpcSerialization()`. Drop-in shape для v3 JSON-провода. |

---

## Архитектура

### Поток RPC-вызова

```mermaid
sequenceDiagram
    participant App  as Клиентское приложение
    participant Px   as RpcDispatchProxy
    participant RC   as RpcClient
    participant MQ   as RabbitMQ
    participant Host as RpcServerHostedService
    participant D    as RpcRequestDispatcher
    participant Impl as Реализация сервиса

    App  ->> Px   : IMyService.AddAsync(a, b)
    Px   ->> RC   : InvokeProxy(interfaceType, method, args)
    RC   ->> MQ   : BasicPublish(RpcRequest)<br/>routingKey = rpc.IMyService<br/>replyTo = rpc-reply-{name}-{guid}

    MQ   ->> Host : Доставка в очередь запросов
    Host ->> D    : DispatchAsync(RpcRequest)
    D    ->> Impl : AddAsync(a, b)
    Impl -->> D   : result
    D    -->> Host: RpcResponse { Success=true, Result }

    Host ->> MQ   : BasicPublish(RpcResponse)<br/>routingKey = replyTo<br/>correlationId = requestId
    MQ   -->> RC  : Доставка ответа
    RC   -->> Px  : десериализованный результат
    Px   -->> App : Task~int~ выполнен
```

### Структура solution

```
RabbitMq.RPC/
├─ AsbtCore.Broker.Core/                            базовые контракты и IRpcSerializer
├─ AsbtCore.Broker.RabbitMq/                        транспортный слой RabbitMQ.Client
├─ AsbtCore.Broker.Client/                          фабрика прокси, RpcClientBuilder и DI
├─ AsbtCore.Broker.Server/                          диспетчер, реестр, RpcServerBuilder
├─ AsbtCore.Broker.Serialization.XPacketRpc/        бинарный IRpcSerializer-адаптер (по умолчанию)
├─ AsbtCore.Broker.Serialization.SystemTextJson/    JSON IRpcSerializer-адаптер (v3-compat)
└─ Tests/
   ├─ AsbtCore.Broker.Core.Tests/
   ├─ AsbtCore.Broker.ClientServer.Tests/
   ├─ AsbtCore.Broker.Serialization.SystemTextJson.Tests/
   └─ AsbtCore.Broker.Serialization.XPacketRpc.Tests/
```

---

## Конфигурация (`RpcOptions`, секция `RabbitMqRpc`)

```json
{
  "RabbitMqRpc": {
    "HostName": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "UserName": "guest",
    "Password": "guest",
    "ClientProvidedName": "my-app",
    "RoutePrefix": "rpc.",
    "PrefetchCount": 1,
    "DefaultTimeoutSeconds": 30
  }
}
```

---

## Пример использования

### 1. Общий контракт

```csharp
// MyApp.Contracts.csproj — без зависимостей от broker
public interface ICalculatorService
{
    Task<int>     AddAsync(int a, int b);
    Task<UserDto> GetUserAsync(Guid id);
}

public sealed record UserDto(Guid Id, string Name);
```

### 2. Сервер

```csharp
// Program.cs (ASP.NET Core / Worker Service)
using AsbtCore.Broker.Server;
using AsbtCore.Broker.Serialization.XPacketRpc;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRabbitRpcServer(builder.Configuration)
    .UseXPacketRpcSerialization()                   // <-- обязательно с v4.0
    .Register<ICalculatorService, CalculatorService>();

var app = builder.Build();
app.Run();

public sealed class CalculatorService : ICalculatorService
{
    public Task<int>     AddAsync(int a, int b) => Task.FromResult(a + b);
    public Task<UserDto> GetUserAsync(Guid id)  => Task.FromResult(new UserDto(id, "Alice"));
}
```

> DTO (`UserDto`, и т.п.) должны быть видны генератору XPacketRpc. В проекте контрактов
> сошлитесь на `XPacketRpc.Generators` как на analyzer и вызовите `XPRpc.Touch<T>()`
> один раз на каждый DTO из `[ModuleInitializer]`. JSON-пользователи (адаптер
> `.UseJsonRpcSerialization()`) этого делать не должны — STJ рефлектит DTO в рантайме.

### 3. Клиент

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Serialization.XPacketRpc;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddRabbitRpcClient(builder.Configuration)
    .UseXPacketRpcSerialization()                   // <-- обязательно с v4.0
    .AddProxy<ICalculatorService>();                // <-- в v3 был AddRpcProxy<T>

var host = builder.Build();

var calc = host.Services.GetRequiredService<ICalculatorService>();
var sum  = await calc.AddAsync(2, 3);               // → 5
var user = await calc.GetUserAsync(Guid.NewGuid()); // → UserDto
```

---

## Обработка ошибок

```mermaid
flowchart TD
    MSG([Входящее сообщение]) --> DESER{Десериализация\nRpcRequest?}

    DESER -->|OK| LOOKUP{Найти сервис\nи метод?}
    DESER -->|Ошибка — poison| DLQ[("route.dead\n(DLQ)")]

    LOOKUP -->|Найден| INVOKE{Вызов\nреализации}
    LOOKUP -->|Не найден| ERRRESP["RpcResponse\ncode: service_not_found\nили method_not_found"]

    INVOKE -->|OK| SUCC["RpcResponse\nSuccess = true"]
    INVOKE -->|Исключение| INVOCERR["RpcResponse\ncode: invocation_error"]

    SUCC     --> PUBLISH[Публикация в replyTo]
    ERRRESP  --> PUBLISH
    INVOCERR --> PUBLISH
    PUBLISH  --> ACK[BasicAck]
    DLQ      --> ACK

    style DLQ      fill:#ff6b6b,color:#fff,stroke:#e03131
    style SUCC     fill:#69db7c,color:#fff,stroke:#2f9e44
    style ERRRESP  fill:#ffd43b,stroke:#f08c00
    style INVOCERR fill:#ffd43b,stroke:#f08c00
```

Исключения сервера сериализуются и пробрасываются на клиенте как `RpcRemoteException`:

```csharp
try
{
    var result = await calc.AddAsync(1, 2);
}
catch (RpcRemoteException ex)
{
    Console.WriteLine(ex.RemoteExceptionType); // напр. "System.InvalidOperationException"
    Console.WriteLine(ex.RemoteCode);          // "invocation_error"
    Console.WriteLine(ex.RemoteDetails);       // стек-трейс сервера
}
catch (TaskCanceledException)
{
    // DefaultTimeoutSeconds превышен
}
```

---

## Надёжность сообщений и DLQ

Каждый RPC-маршрут получает сопутствующую durble dead-letter очередь `{route}.dead`.

```mermaid
flowchart LR
    subgraph queues["Очереди RabbitMQ (на каждый зарегистрированный сервис)"]
        RQ[("rpc.IMyService\n(очередь запросов)")]
        DQ[("rpc.IMyService.dead\n(dead-letter очередь)")]
    end

    CLIENT["Клиент"] -->|"publish + replyTo"| RQ
    RQ -->|корректное сообщение| SERVER["Сервер\nДиспетчер"]
    SERVER -->|"BasicAck"| RQ
    RQ -->|poison / недоставляемое| DQ

    SERVER -->|"ответ"| REPLYQ[("rpc-reply-{name}-{guid}\n(reply-очередь клиента)")]
    REPLYQ --> CLIENT

    style RQ    fill:#4dabf7,color:#fff,stroke:#339af0
    style DQ    fill:#ff6b6b,color:#fff,stroke:#e03131
    style REPLYQ fill:#69db7c,color:#fff,stroke:#2f9e44
```

Poison-сообщения (неверный payload, нераспознанный тип, внутренняя ошибка диспетчера) перемещаются в `*.dead` **после единственной попытки** — бесконечных циклов requeue нет. Следите за глубиной очереди `*.dead` для алертинга.

---

## Как добавить новый RPC-сервис

1. **Контракт** — добавить интерфейс (`Task` / `Task<T>` методы) + DTO в общий проект `*.Contracts`.
2. **Сервер** — реализовать и зарегистрировать:
   ```csharp
   services.AddRabbitRpcServer(configuration)
           .Register<IMyService, MyServiceImpl>();
   ```
3. **Клиент** — зарегистрировать прокси:
   ```csharp
   services.AddRabbitRpcClient(configuration)
           .AddRpcProxy<IMyService>();
   ```
4. Обе стороны должны использовать одинаковый `RoutePrefix` и namespace интерфейса. Ключ маршрутизации = `RoutePrefix + typeof(T).FullName`.

### Работа с DTO из сторонних сборок (MemoryPack)

Если DTO поставляются в скомпилированных библиотеках, которые нельзя
изменить, адаптер `RabbitRpc.Serialization.MemoryPack` поддерживает их
без атрибута `[MemoryPackable]`. По умолчанию работает lazy-обнаружение.

Для путей, чувствительных к latency, используйте `PrewarmAssembly` /
`PrewarmType` — это переносит стоимость подготовки формат-делегатов
на старт.

Для полиморфных базовых типов нужно явно зарегистрировать union — без
этого MemoryPack не сможет восстановить derived-тип из ссылки на базу.

```csharp
services.AddRabbitRpcServer(configuration)
    .UseMemoryPackRpcSerialization(opt =>
    {
        opt.PrewarmAssembly(typeof(UserDto).Assembly);
        opt.RegisterUnion<Animal>(b => b
            .Add<Cat>(tag: 1)
            .Add<Dog>(tag: 2));
    });
```

Производительность: рефлекшен-форматтер **предположительно** в 2-4 раза медленнее
нативного source-gen `[MemoryPackable]`, но всё ещё быстрее JSON (оценка
основана на архитектурных соображениях; бенчмарка этого пути ещё не было).
AOT/trim сценарии на этом пути не поддерживаются — для них используйте
`[MemoryPackable]`.

---

## Тестирование

Тесты используют **TUnit** + **Moq** и запускаются без реального RabbitMQ broker.

```bash
dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj
dotnet run --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj
```

**83 теста** — 45 в `Core.Tests`, 38 в `ClientServer.Tests`.

Покрытие (только unit-тесты, без реального broker):

```mermaid
xychart-beta horizontal
    title "Покрытие строк по пакетам"
    x-axis ["Core", "Client", "Server", "RabbitMq*"]
    y-axis "%" 0 --> 100
    bar [100, 97, 95, 41]
```

> \* Транспортные классы RabbitMq (`RabbitMqRpcTransport`, `RabbitMqConnectionProvider`) требуют живого broker; оставшееся покрытие — территория интеграционных тестов.

---

## Требования

- .NET 10
- RabbitMQ 3.12+ / RabbitMQ.Client 7.x

---

## Миграция v2 → v3

v3.0.0 — релиз с фокусом на надёжность, содержит **ломающие изменения wire-формата и поведения**. v2.x и v3.x **несовместимы** — обновляйте клиенты и серверы одновременно.

### Изменение wire-формата

Имена типов параметров и результатов теперь используют стабильную форму `Namespace.TypeName, AssemblySimpleName` без `Version`, `Culture` и `PublicKeyToken`. Плановые версионные бампы контрактных сборок больше не ломают wire-формат.

Клиент v2 **не может** общаться с сервером v3 (и наоборот) — поиск ключа метода завершится ошибкой `method_not_found`.

### Изменения поведения

```mermaid
graph LR
    subgraph v2["Поведение v2"]
        V2A["Переподключение broker\n→ зависшие задачи\nдо завершения процесса"]
        V2B["Broker nack при публикации\n→ TaskCanceledException\nпосле полного таймаута"]
        V2C["Poison-сообщение\n→ BasicNack(requeue:true)\n→ бесконечный цикл"]
        V2D["Reply-очередь\nпаттерн amq.gen-*"]
    end
    subgraph v3["Поведение v3"]
        V3A["Переподключение broker\n→ TransportReconnectedException\n(немедленно, с retry)"]
        V3B["Broker nack при публикации\n→ RpcPublishFailedException\n(немедленно)"]
        V3C["Poison-сообщение\n→ route.dead DLQ\n(одна попытка)"]
        V3D["Reply-очередь\nrpc-reply-{name}-{guid}"]
    end

    V2A -.->|обновлено до| V3A
    V2B -.->|обновлено до| V3B
    V2C -.->|обновлено до| V3C
    V2D -.->|обновлено до| V3D

    style V3A fill:#69db7c,color:#fff,stroke:#2f9e44
    style V3B fill:#69db7c,color:#fff,stroke:#2f9e44
    style V3C fill:#69db7c,color:#fff,stroke:#2f9e44
    style V3D fill:#69db7c,color:#fff,stroke:#2f9e44
    style V2A fill:#ff6b6b,color:#fff,stroke:#e03131
    style V2B fill:#ff6b6b,color:#fff,stroke:#e03131
    style V2C fill:#ff6b6b,color:#fff,stroke:#e03131
    style V2D fill:#ff6b6b,color:#fff,stroke:#e03131
```

### Действия оператора

1. Обновите пакеты клиента и сервера **одновременно**.
2. Ожидайте появления новых очередей `*.dead` на каждый RPC-маршрут в broker — настройте политики TTL/max-length по необходимости.
3. Добавьте `catch (TransportReconnectedException)` и/или `catch (RpcPublishFailedException)` там, где вы awaite методы прокси.
4. Обновите фильтры мониторинга, которые совпадали со старым паттерном reply-очереди `amq.gen-*`.

---

## Миграция v3.0 → v3.1

- **Серверная диспетчеризация сообщений теперь параллельная по умолчанию.** `RpcOptions.ConsumerDispatchConcurrency` по умолчанию равен `PrefetchCount`. Существующие хендлеры должны быть потокобезопасны. Задайте `RpcOptions.ConsumerDispatchConcurrency = 1`, чтобы вернуть последовательное поведение v3.0.
- В `IRpcTransportHost` добавлен `StopAsync(CancellationToken)` (default interface method). Кастомные транспорты должны переопределить метод, чтобы дренировать in-flight хендлеры перед disposal.
- `IRpcTransportHost.Dispose()` теперь sync-over-async last-resort путь. Предпочитайте `DisposeAsync()` (или disposal через DI).
- `RpcRequest.RequestId` больше не инициализируется автоматически в property initializer. Клиентский код, конструирующий `RpcRequest` напрямую, должен задавать `RequestId` явно.
- Обработка poison reply: `OnResponseReceivedAsync` теперь пробрасывает ошибки десериализации в ожидающий caller вместо тихого логирования и ожидания timeout.

---

## Миграция v3.1 → v4.0

v4.0 вводит подключаемый слой сериализации и поставляет бинарный wire-формат по умолчанию. **Wire-формат несовместим с v3.x — обновляйте клиент и сервер вместе.** Дренируйте очереди `rpc.*` до нуля или запланируйте окно даунтайма перед обменом деплоев.

### Что изменилось

1. **Wire-формат бинарный по умолчанию.** Клиенты v3.x не могут общаться с серверами v4 (и наоборот) вне зависимости от выбора адаптера — фрейминг сменился.
2. **Adapter-пакет сериализации теперь обязателен.** Установите **один** из:
   - `RabbitRpc.Serialization.XPacketRpc` — бинарный, рекомендуется для новых и высокопроизводительных деплоев.
   - `RabbitRpc.Serialization.SystemTextJson` — JSON-на-проводе (совместимая форма с v3 payload). Выбирайте, если нельзя сразу отказаться от v3 продьюсеров/консьюмеров и нужно faithful JSON-поведение.
3. **Новый DI-вызов обязателен.** Добавьте `.UseXPacketRpcSerialization()` или `.UseJsonRpcSerialization()` в builder. Старт падает с `OptionsValidationException`, если `IRpcSerializer` не зарегистрирован.
4. **`AddRabbitRpcClient(cfg)` возвращает `RpcClientBuilder`**, не `IServiceCollection`. Цепляйте `.UseXPacketRpcSerialization()` затем `.AddProxy<T>()` (v3-расширение `AddRpcProxy<T>()` удалено).
5. **Core-типы удалены**: `AsbtCore.Broker.Core.Serialization.{JsonRpcSerializer, RpcJson, RpcSerializationHelper, AddRpcSerialization}`. Они переехали в SystemTextJson adapter; новый namespace — `AsbtCore.Broker.Serialization.SystemTextJson`.
6. **`RpcRequest.Arguments[i].Payload`**: `JsonElement` → `ReadOnlyMemory<byte>`. Касается только кода, который строит `RpcRequest` напрямую (кастомные транспорты).
7. **`RpcResponse.Result`**: `JsonElement?` → `ReadOnlyMemory<byte>?`.
8. **`IRpcSerializer` расширен** до четырёх методов (envelope `Serialize<T>` / `Deserialize<T>` над `RpcRequest`/`RpcResponse`, плюс `SerializeFragment` / `DeserializeFragment` над per-arg payload). Кастомные реализации требуют обновления.

### Было → стало

```csharp
// v3.1
services.AddRpcSerialization<JsonRpcSerializer>();          // namespace AsbtCore.Broker.Core.Serialization (удалён)
services.AddRabbitRpcClient(configuration)
        .AddRpcProxy<IMyService>();                         // возвращал IServiceCollection
```

```csharp
// v4.0 — бинарный (рекомендуется)
services.AddRabbitRpcClient(configuration)                  // теперь возвращает RpcClientBuilder
        .UseXPacketRpcSerialization()
        .AddProxy<IMyService>();
```

```csharp
// v4.0 — JSON drop-in
services.AddRabbitRpcClient(configuration)
        .UseJsonRpcSerialization()
        .AddProxy<IMyService>();
```

### Настройка DTO для XPacketRpc adapter

Бинарный adapter полагается на Roslyn source generator, который эмитит per-DTO codec-ов на этапе компиляции. Генератор видит только DTO, достижимые из call site `XPRpc.Touch<T>()` в **той же compilation**, что и DTO. В вашем проекте контрактов:

1. Сошлитесь на пакет генератора как на analyzer:
   ```xml
   <ProjectReference Include="..\.external\XProtokol\XPacketRpc\XPacketRpc.csproj" />
   <ProjectReference Include="..\.external\XProtokol\XPacketRpc.Generators\XPacketRpc.Generators.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```
2. Touch каждый DTO из module initializer:
   ```csharp
   using System.Runtime.CompilerServices;
   using XPacketRpc;

   internal static class GeneratorTouchSites
   {
       [ModuleInitializer]
       internal static void TouchAll()
       {
           XPRpc.Touch<UserDto>();
           XPRpc.Touch<OrderDto>();
       }
   }
   ```

JSON adapter не имеет эквивалентного шага — `System.Text.Json` рефлектит DTO в рантайме.

### Действия оператора

1. Обновите клиента и сервера **одновременно**; дренируйте `rpc.*` очереди до нуля.
2. Выберите один adapter-пакет (`RabbitRpc.Serialization.XPacketRpc` или `RabbitRpc.Serialization.SystemTextJson`) и добавьте его в каждый проект клиента/сервера.
3. Везде, где `AddRabbitRpcClient(cfg)` цепляется с `.AddRpcProxy<T>()`, замените на `.UseXxxSerialization().AddProxy<T>()`.
4. Везде, где `AddRabbitRpcServer(cfg)` цепляется с `.Register<I,Impl>()`, добавьте `.UseXxxSerialization()` перед `.Register`.
5. При выборе XPacketRpc — выполните DTO-настройку выше в проекте контрактов.
