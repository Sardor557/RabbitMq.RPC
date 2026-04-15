# RabbitRpc.Client

Клиентская часть RabbitMQ RPC для .NET 8. Предоставляет типизированные прокси: вы инжектите интерфейс контракта и вызываете методы как обычный сервис — запрос уходит в RabbitMQ и возвращает ответ от сервера.

## Установка

```bash
dotnet add package RabbitRpc.Client

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
    "ClientProvidedName": "rabbit-rpc-client",
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

Зарегистрируйте клиент и прокси в `Program.cs`:

```csharp
using AsbtCore.Broker.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRabbitRpcClientPackage(builder.Configuration);
builder.Services.AddRabbitRpcProxy<IMathService>(TimeSpan.FromSeconds(30));
```

Используйте прокси как обычный DI-сервис:

```csharp
public class CalculatorController : ControllerBase
{
    private readonly IMathService _math;

    public CalculatorController(IMathService math) => _math = math;

    [HttpGet("add")]
    public Task<int> Add(int a, int b, CancellationToken ct)
        => _math.AddAsync(a, b, ct);
}
```

Параметр `timeout` у `AddRabbitRpcProxy<T>` задаёт таймаут ожидания ответа (по умолчанию — значение из настроек транспорта).

## См. также

- [RabbitRpc.Server](https://www.nuget.org/packages/RabbitRpc.Server) — серверная часть для размещения обработчиков.
