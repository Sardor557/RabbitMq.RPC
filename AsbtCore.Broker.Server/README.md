# RabbitRpc.Server

Серверная часть RabbitMQ RPC для .NET 8. Регистрирует обработчики RPC-методов через DI и запускает HostedService, слушающий очередь RabbitMQ.

## Установка

```bash
dotnet add package RabbitRpc.Server

dotnet pack -c Release
```

## Конфигурация

В `appsettings.json` добавьте секцию `RabbitMqRpc`:

```json
{
  "RabbitMqRpc": {
    "HostName": "localhost",
    "Port": 5672,
    "VirtualHost": "/",
    "UserName": "guest",
    "Password": "guest",
    "ClientProvidedName": "rabbit-rpc-server",
    "PrefetchCount": 1
  }
}
```

## Использование

Определите интерфейс контракта (общий для клиента и сервера):

```csharp
public interface IMathService
{
    Task<int> AddAsync(int a, int b, CancellationToken ct = default);
}
```

Реализуйте его на стороне сервера:

```csharp
public class MathService : IMathService
{
    public Task<int> AddAsync(int a, int b, CancellationToken ct = default)
        => Task.FromResult(a + b);
}
```

Зарегистрируйте в `Program.cs`:

```csharp
using AsbtCore.Broker.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddRabbitRpcServerPackage(builder.Configuration)
    .Register<IMathService, MathService>();

await builder.Build().RunAsync();
```

HostedService стартует автоматически и начинает принимать RPC-запросы.

## См. также

- [RabbitRpc.Client](https://www.nuget.org/packages/RabbitRpc.Client) — клиентская часть с типизированными прокси.
