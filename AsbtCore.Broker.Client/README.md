# RabbitRpc.Client

The client-side library for RabbitMQ RPC in .NET. Provides typed proxies: inject a contract interface and call its methods like any regular service — the request is sent to RabbitMQ and the response is returned from the server.

## Installation

Install the package from NuGet:

```bash
dotnet add package RabbitRpc.Client
```

## Configuration

Add the `RabbitMqRpc` section to your `appsettings.json`:

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

## Usage

Define a contract interface (shared between client and server):

```csharp
public interface IMathService
{
    Task<int> AddAsync(int a, int b, CancellationToken ct = default);
}
```

Register the client and proxy in `Program.cs`:

```csharp
using AsbtCore.Broker.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRabbitRpcClientPackage(builder.Configuration);
builder.Services.AddRabbitRpcProxy<IMathService>(TimeSpan.FromSeconds(30));
```

Inject and use the proxy like any other DI service:

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

The `timeout` parameter in `AddRabbitRpcProxy<T>` sets the response wait timeout (defaults to the value from transport settings).

## Building a NuGet Package

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) installed
- Package metadata configured in the `.csproj` file:

```xml
<PropertyGroup>
  <PackageId>RabbitRpc.Client</PackageId>
  <Version>1.0.0</Version>
  <Authors>Your Name</Authors>
  <Description>Client-side RabbitMQ RPC library for .NET</Description>
  <PackageTags>rabbitmq;rpc;client</PackageTags>
  <RepositoryUrl>https://github.com/Sardor557/RabbitMq.RPC</RepositoryUrl>
</PropertyGroup>
```

### Pack

Build and create the `.nupkg` file:

```bash
dotnet pack -c Release
```

The output package will be placed in `bin/Release/`.

### Publish to NuGet.org

```bash
dotnet nuget push bin/Release/RabbitRpc.Client.*.nupkg --api-key <YOUR_API_KEY> --source https://api.nuget.org/v3/index.json
```

Replace `<YOUR_API_KEY>` with your API key from [nuget.org](https://www.nuget.org/account/apikeys).

### Publish to a local or private feed

```bash
dotnet nuget push bin/Release/RabbitRpc.Client.*.nupkg --source <FEED_URL>
```

## See Also

- [RabbitRpc.Server](https://www.nuget.org/packages/RabbitRpc.Server) — server-side library for hosting RPC handlers.
