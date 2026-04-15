# AsbtCore.Broker — RabbitMQ RPC

[English version](README.md)

Легковесный RPC-фреймворк поверх RabbitMQ для .NET 8: типобезопасные контракты через C#-интерфейсы, DI-интеграция на клиенте и сервере, JSON-сериализация, reply-queue паттерн.

Репозиторий публикует два потребительских NuGet-пакета: **`AsbtCore.Broker.Client`** и **`AsbtCore.Broker.Server`**. Остальные сборки (`Core`, `RabbitMq`) подтягиваются транзитивно.

## Установка

В целевом приложении установить нужный пакет — зависимости (`AsbtCore.Broker.Core`, `AsbtCore.Broker.RabbitMq`) подтягиваются автоматически.

**Клиентское приложение** (вызывает удалённые сервисы):

```bash
dotnet add package AsbtCore.Broker.Client
```

**Серверное приложение** (хостит реализации):

```bash
dotnet add package AsbtCore.Broker.Server
```

**Общий проект контрактов** — обычная class library с интерфейсами и DTO, на неё ссылаются обе стороны. Ссылка на `AsbtCore.Broker.*` в ней не нужна.

Типовая структура solution:

```
MySolution/
├─ MyApp.Contracts/        class library (интерфейсы + DTO)
├─ MyApp.Server/           ссылается на AsbtCore.Broker.Server + Contracts
└─ MyApp.Client/           ссылается на AsbtCore.Broker.Client + Contracts
```

## Архитектура

```
┌─────────────────┐        RabbitMQ         ┌─────────────────┐
│   Client host   │       (RPC exchange)    │   Server host   │
│                 │                         │                 │
│  IMyService ──► RpcProxy ──► RpcClient ──►│ request queue ──┼──► RpcRequestDispatcher
│                                           │                 │         │
│  result ◄── reply queue ◄── Transport ◄───┼── reply props   │         ▼
│                                           │                 │    impl.Method(args)
└─────────────────┘                         └─────────────────┘
```

### Пакеты (solution `RabbitMq.RPC.sln`)

| Проект | Назначение |
|---|---|
| `AsbtCore.Broker.Core` | Контракты (`RpcRequest`/`RpcResponse`), `IRpcTransport`, `IRpcSerializer`, `IRpcRouteResolver`, `RpcOptions`, `RpcRemoteException`. |
| `AsbtCore.Broker.RabbitMq` | `RabbitMqRpcTransport` (клиент), `RabbitMqRpcTransportHost` (сервер), `IRabbitMqConnectionProvider`. |
| `AsbtCore.Broker.Client` | `RpcClient`, `RpcProxyFactory` (`DispatchProxy`), DI: `AddRabbitRpcClient` / `AddRpcProxy<T>`. |
| `AsbtCore.Broker.Server` | `RpcServerBuilder`, `RpcServerRegistry`, `RpcRequestDispatcher`, `RpcServerHostedService`, DI: `AddRabbitRpcServer`. |
| `Tests/*` | MSTest + Moq: 42 теста, изоляция от реального RabbitMQ. |

### Поток вызова

1. Клиент вызывает метод на прокси → `RpcProxyFactory` упаковывает аргументы в `RpcRequest`.
2. `RpcClient` → `RabbitMqRpcTransport.SendAsync` публикует сообщение в exchange по ключу `RoutePrefix + FullName(interface)`, задаёт `CorrelationId` и `ReplyTo` (эксклюзивная очередь клиента).
3. Сервер: `RpcServerHostedService` поднимает `IRpcTransportHost`, на каждое входящее сообщение вызывает `RpcRequestDispatcher`, который ищет реализацию в `RpcServerRegistry` и вызывает метод через reflection.
4. Результат/исключение → `RpcResponse`, публикуется в `ReplyTo` с тем же `CorrelationId`. Исключения сервера → `RpcRemoteException` на клиенте.

## Конфигурация (`RpcOptions`, секция `Rpc`)

```json
{
  "Rpc": {
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

## Пример использования

### 1. Общий контракт

```csharp
// Contracts.csproj
public interface ICalculatorService
{
    Task<int> AddAsync(int a, int b);
    Task<UserDto> GetUserAsync(Guid id);
}

public sealed record UserDto(Guid Id, string Name);
```

### 2. Сервер

```csharp
// Program.cs (ASP.NET / Worker)
using AsbtCore.Broker.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRabbitRpcServer(builder.Configuration)
    .Register<ICalculatorService, CalculatorService>();

var app = builder.Build();
app.Run();

public sealed class CalculatorService : ICalculatorService
{
    public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
    public Task<UserDto> GetUserAsync(Guid id) => Task.FromResult(new UserDto(id, "Alice"));
}
```

### 3. Клиент

```csharp
using AsbtCore.Broker.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddRabbitRpcClient(builder.Configuration)
    .AddRpcProxy<ICalculatorService>();

var host = builder.Build();

var calc = host.Services.GetRequiredService<ICalculatorService>();
var sum  = await calc.AddAsync(2, 3);         // 5
var user = await calc.GetUserAsync(Guid.NewGuid());
```

## Как подключить RPC к своему проекту

1. Создать общую class library `MyApp.Contracts` (без ссылок на broker).
2. В `MyApp.Server`: `dotnet add package AsbtCore.Broker.Server` + ссылка на `MyApp.Contracts`.
3. В `MyApp.Client`: `dotnet add package AsbtCore.Broker.Client` + ссылка на `MyApp.Contracts`.
4. Добавить секцию `Rpc` в `appsettings.json` с обеих сторон (см. [Конфигурация](#конфигурация-rpcoptions-секция-rpc)).
5. Подключить DI (см. [Пример использования](#пример-использования)).

## Как добавить новый RPC-сервис

1. **Контракт** — в общий проект `*.Contracts` добавить интерфейс с `Task`/`Task<T>` методами и DTO-типы (сериализуются через `System.Text.Json`).
2. **Сервер** — реализовать интерфейс и зарегистрировать:
   ```csharp
   services.AddRabbitRpcServer(configuration)
           .Register<IMyService, MyService>();
   ```
3. **Клиент** — подключить прокси:
   ```csharp
   services.AddRabbitRpcClient(configuration)
           .AddRpcProxy<IMyService>();
   ```
4. Клиент и сервер должны использовать одинаковый `RoutePrefix` и namespace интерфейса (ключ маршрута = `RoutePrefix + typeof(T).FullName`).

## Обработка ошибок

Исключение в реализации сервера сериализуется и возбуждается на клиенте как `RpcRemoteException`:

```csharp
try { await calc.AddAsync(1, 2); }
catch (RpcRemoteException ex)
{
    // ex.RemoteExceptionType, ex.RemoteCode, ex.RemoteDetails
}
```

Таймаут: `DefaultTimeoutSeconds` → `TaskCanceledException`.

## Тестирование

```bash
dotnet test RabbitMq.RPC.sln
```

42 теста (Core.Tests — сериализация/роутинг/транспорт через мок `IChannel`; ClientServer.Tests — RpcClient/Proxy/Registry/Dispatcher/HostedService). Реальное подключение к RabbitMQ не требуется.

## Требования

- .NET 8
- RabbitMQ 3.12+ (RabbitMQ.Client 7.x)
