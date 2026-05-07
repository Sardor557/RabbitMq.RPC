# RabbitRpc.Server

The server-side library for RabbitMQ RPC in .NET. Registers RPC method handlers via DI and starts a hosted service that listens to a RabbitMQ queue.

## Installation

Install the package from NuGet:

```bash
dotnet add package RabbitRpc.Server
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
    "ClientProvidedName": "rabbit-rpc-server",
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

Implement it on the server side:

```csharp
public class MathService : IMathService
{
    public Task<int> AddAsync(int a, int b, CancellationToken ct = default)
        => Task.FromResult(a + b);
}
```

Register the server and handlers in `Program.cs`:

```csharp
using AsbtCore.Broker.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddRabbitRpcServerPackage(builder.Configuration)
    .Register<IMathService, MathService>();

await builder.Build().RunAsync();
```

The hosted service starts automatically and begins accepting RPC requests.

## Building a NuGet Package

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) installed
- Package metadata configured in the `.csproj` file:

```xml
<PropertyGroup>
  <PackageId>RabbitRpc.Server</PackageId>
  <Version>1.0.0</Version>
  <Authors>Your Name</Authors>
  <Description>Server-side RabbitMQ RPC library for .NET</Description>
  <PackageTags>rabbitmq;rpc;server</PackageTags>
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
dotnet nuget push bin/Release/RabbitRpc.Server.*.nupkg --api-key <YOUR_API_KEY> --source https://api.nuget.org/v3/index.json
```

Replace `<YOUR_API_KEY>` with your API key from [nuget.org](https://www.nuget.org/account/apikeys).

### Publish to a local or private feed

```bash
dotnet nuget push bin/Release/RabbitRpc.Server.*.nupkg --source <FEED_URL>
```

## See Also

- [RabbitRpc.Client](https://www.nuget.org/packages/RabbitRpc.Client) — client-side library with typed proxies.
