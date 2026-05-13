# MemoryPack Serialization Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a MemoryPack binary serialization adapter to `AsbtCore.Broker`, ship it as NuGet `RabbitRpc.Serialization.MemoryPack`, and migrate demo apps from XPacketRpc to MemoryPack.

**Architecture:** New project `AsbtCore.Broker.Serialization.MemoryPack` implements `IRpcSerializer` via hand-written `IMemoryPackFormatter<T>` for the four Core wire types (`RpcRequest`, `RpcArgument`, `RpcResponse`, `RpcError`). Formatters are registered globally in the serializer's static constructor via `MemoryPackFormatterProvider.Register()`. Core projects are never touched.

**Tech Stack:** MemoryPack (Cysharp, NuGet `*`), TUnit (test framework, run via `dotnet run`), Moq 4.x, .NET 10 / C# 13.

---

## File Map

**Create:**
- `AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj`
- `AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcSerializer.cs`
- `AsbtCore.Broker.Serialization.MemoryPack/Formatters/RpcContractFormatters.cs`
- `AsbtCore.Broker.Serialization.MemoryPack/DependencyInjection/MemoryPackRpcServiceCollectionExtensions.cs`
- `AsbtCore.Broker.Serialization.MemoryPack/README.md`
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj`
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/MemoryPackRpcSerializerTests.cs`
- `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/UseMemoryPackRpcSerializationTests.cs`

**Modify:**
- `RabbitMq.RPC.sln` — add 2 new projects
- `Test.Contracts/IUserService.cs` — add `[MemoryPackable]` + `partial` to `UserDto`
- `Test.Contracts/Contracts.csproj` — swap XPacketRpc.Generators for MemoryPack NuGet
- `Test.Broker.API/Broker.API.csproj` — swap XPacketRpc ref → MemoryPack
- `Test.Broker.API/Program.cs` — `UseXPacketRpcSerialization()` → `UseMemoryPackRpcSerialization()`
- `Test.Client/Client.csproj` — swap XPacketRpc ref → MemoryPack
- `Test.Client/Program.cs` — `UseXPacketRpcSerialization()` → `UseMemoryPackRpcSerialization()`

**Delete:**
- `Test.Contracts/GeneratorTouchSites.cs`

---

### Task 1: Scaffold adapter project

**Files:**
- Create: `AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj`
- Create: `AsbtCore.Broker.Serialization.MemoryPack/MemoryPackRpcSerializer.cs`
- Create: `AsbtCore.Broker.Serialization.MemoryPack/Formatters/RpcContractFormatters.cs`
- Create: `AsbtCore.Broker.Serialization.MemoryPack/DependencyInjection/MemoryPackRpcServiceCollectionExtensions.cs`
- Create: `AsbtCore.Broker.Serialization.MemoryPack/README.md`

- [ ] **Step 1: Create the project directory**

```bash
mkdir AsbtCore.Broker.Serialization.MemoryPack
mkdir AsbtCore.Broker.Serialization.MemoryPack/Formatters
mkdir AsbtCore.Broker.Serialization.MemoryPack/DependencyInjection
```

- [ ] **Step 2: Create `AsbtCore.Broker.Serialization.MemoryPack.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>

    <PackageId>RabbitRpc.Serialization.MemoryPack</PackageId>
    <Title>RabbitRpc.Serialization.MemoryPack</Title>
    <Description>MemoryPack binary adapter for RabbitRpc — provides IRpcSerializer over MemoryPack with hand-written formatters for Core wire types.</Description>
    <PackageTags>rabbitmq;rpc;dotnet;binary;serialization;memorypack</PackageTags>

    <Version>1.0.0</Version>
    <Authors>AsbtCore</Authors>
    <Company>AsbtCore</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/Sardor557/AsbtCore.Broker</PackageProjectUrl>
    <RepositoryUrl>https://github.com/Sardor557/AsbtCore.Broker</RepositoryUrl>
    <RepositoryType>git</RepositoryType>

    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MemoryPack" Version="*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AsbtCore.Broker.Core\AsbtCore.Broker.Core.csproj" PrivateAssets="all" />
    <ProjectReference Include="..\AsbtCore.Broker.Client\AsbtCore.Broker.Client.csproj" PrivateAssets="all" />
    <ProjectReference Include="..\AsbtCore.Broker.Server\AsbtCore.Broker.Server.csproj" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="AsbtCore.Broker.Serialization.MemoryPack.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create skeleton `MemoryPackRpcSerializer.cs`**

```csharp
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Serialization.MemoryPack.Formatters;
using MemoryPack;

namespace AsbtCore.Broker.Serialization.MemoryPack;

public sealed class MemoryPackRpcSerializer : IRpcSerializer
{
    static MemoryPackRpcSerializer()
    {
        MemoryPackFormatterProvider.Register(new RpcErrorFormatter());
        MemoryPackFormatterProvider.Register(new RpcArgumentFormatter());
        MemoryPackFormatterProvider.Register(new RpcRequestFormatter());
        MemoryPackFormatterProvider.Register(new RpcResponseFormatter());
    }

    public string ContentType => "application/x-memorypack-rpc";

    public ReadOnlyMemory<byte> Serialize<T>(T value)
        => MemoryPackSerializer.Serialize(value);

    public T? Deserialize<T>(ReadOnlyMemory<byte> payload)
        => MemoryPackSerializer.Deserialize<T>(payload.Span);

    public ReadOnlyMemory<byte> SerializeFragment(object? value, Type type)
        => MemoryPackSerializer.Serialize(type, value);

    public object? DeserializeFragment(ReadOnlyMemory<byte> payload, Type type)
        => MemoryPackSerializer.Deserialize(type, payload.Span);
}
```

- [ ] **Step 4: Create skeleton `Formatters/RpcContractFormatters.cs`** (stubs — real implementation comes in Task 3)

```csharp
using System.Buffers;
using AsbtCore.Broker.Core;
using MemoryPack;

namespace AsbtCore.Broker.Serialization.MemoryPack.Formatters;

internal sealed class RpcErrorFormatter : IMemoryPackFormatter<RpcError>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcError? value)
        where TBufferWriter : IBufferWriter<byte>
        => throw new NotImplementedException();

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcError? value)
        => throw new NotImplementedException();
}

internal sealed class RpcArgumentFormatter : IMemoryPackFormatter<RpcArgument>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcArgument? value)
        where TBufferWriter : IBufferWriter<byte>
        => throw new NotImplementedException();

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcArgument? value)
        => throw new NotImplementedException();
}

internal sealed class RpcRequestFormatter : IMemoryPackFormatter<RpcRequest>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcRequest? value)
        where TBufferWriter : IBufferWriter<byte>
        => throw new NotImplementedException();

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcRequest? value)
        => throw new NotImplementedException();
}

internal sealed class RpcResponseFormatter : IMemoryPackFormatter<RpcResponse>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcResponse? value)
        where TBufferWriter : IBufferWriter<byte>
        => throw new NotImplementedException();

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcResponse? value)
        => throw new NotImplementedException();
}
```

- [ ] **Step 5: Create skeleton `DependencyInjection/MemoryPackRpcServiceCollectionExtensions.cs`**

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AsbtCore.Broker.Serialization.MemoryPack;

public static class MemoryPackRpcServiceCollectionExtensions
{
    public static RpcServerBuilder UseMemoryPackRpcSerialization(this RpcServerBuilder builder)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new MemoryPackRpcSerializer());
        return builder;
    }

    public static RpcClientBuilder UseMemoryPackRpcSerialization(this RpcClientBuilder builder)
    {
        builder.Services.TryAddSingleton<IRpcSerializer>(_ => new MemoryPackRpcSerializer());
        return builder;
    }
}
```

- [ ] **Step 6: Create placeholder `README.md`**

```markdown
# RabbitRpc.Serialization.MemoryPack

MemoryPack binary serialization adapter for [RabbitRpc](https://github.com/Sardor557/AsbtCore.Broker).

## Usage

```csharp
// Server
services.AddRabbitRpcServer(configuration)
    .UseMemoryPackRpcSerialization()
    .Register<IMyService, MyServiceImpl>();

// Client
services.AddRabbitRpcClient(configuration)
    .UseMemoryPackRpcSerialization()
    .AddRpcProxy<IMyService>();
```

## DTO Requirements

DTOs must be decorated with `[MemoryPackable]` and declared `partial`:

```csharp
[MemoryPackable]
public sealed partial class MyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```
```

- [ ] **Step 7: Add project to solution**

```bash
dotnet sln RabbitMq.RPC.sln add AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj
```

- [ ] **Step 8: Verify adapter project builds**

```bash
dotnet build AsbtCore.Broker.Serialization.MemoryPack/AsbtCore.Broker.Serialization.MemoryPack.csproj
```

Expected: Build succeeded, 0 errors.

---

### Task 2: Scaffold test project

**Files:**
- Create: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj`
- Create: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/MemoryPackRpcSerializerTests.cs`
- Create: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/UseMemoryPackRpcSerializationTests.cs`

- [ ] **Step 1: Create test project directory**

```bash
mkdir Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests
```

- [ ] **Step 2: Create `AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TUnit" Version="*" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="MemoryPack" Version="*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\AsbtCore.Broker.Serialization.MemoryPack\AsbtCore.Broker.Serialization.MemoryPack.csproj" />
    <ProjectReference Include="..\..\AsbtCore.Broker.Core\AsbtCore.Broker.Core.csproj" />
    <ProjectReference Include="..\..\AsbtCore.Broker.Client\AsbtCore.Broker.Client.csproj" />
    <ProjectReference Include="..\..\AsbtCore.Broker.Server\AsbtCore.Broker.Server.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create stub `MemoryPackRpcSerializerTests.cs`** (one failing test to verify wiring)

```csharp
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Serialization.MemoryPack;
using MemoryPack;

namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

public class MemoryPackRpcSerializerContentTypeTests
{
    [Test]
    public async Task ContentType_IsMemoryPackRpc()
    {
        await Assert.That(new MemoryPackRpcSerializer().ContentType)
            .IsEqualTo("application/x-memorypack-rpc");
    }
}
```

- [ ] **Step 4: Create stub `UseMemoryPackRpcSerializationTests.cs`**

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

public class UseMemoryPackRpcSerializationTests
{
    [Test]
    public async Task ServerBuilder_Registers_MemoryPackRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcServerBuilder(services);
        builder.UseMemoryPackRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<MemoryPackRpcSerializer>();
    }

    [Test]
    public async Task ClientBuilder_Registers_MemoryPackRpcSerializer()
    {
        var services = new ServiceCollection();
        var builder = new RpcClientBuilder(services);
        builder.UseMemoryPackRpcSerialization();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetService<IRpcSerializer>();
        await Assert.That(serializer).IsNotNull();
        await Assert.That(serializer).IsTypeOf<MemoryPackRpcSerializer>();
    }
}
```

- [ ] **Step 5: Add test project to solution**

```bash
dotnet sln RabbitMq.RPC.sln add Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj
```

- [ ] **Step 6: Verify test project builds**

```bash
dotnet build Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Run ContentType test — expect PASS**

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "*/*/MemoryPackRpcSerializerContentTypeTests/*"
```

Expected: 1 passed (ContentType returns the correct constant, no formatter needed).

---

### Task 3: TDD — envelope formatters

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/MemoryPackRpcSerializerTests.cs`
- Modify: `AsbtCore.Broker.Serialization.MemoryPack/Formatters/RpcContractFormatters.cs`

- [ ] **Step 1: Add failing envelope tests to `MemoryPackRpcSerializerTests.cs`**

Replace the file with:

```csharp
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Serialization.MemoryPack;
using MemoryPack;

namespace AsbtCore.Broker.Serialization.MemoryPack.Tests;

public class MemoryPackRpcSerializerContentTypeTests
{
    [Test]
    public async Task ContentType_IsMemoryPackRpc()
    {
        await Assert.That(new MemoryPackRpcSerializer().ContentType)
            .IsEqualTo("application/x-memorypack-rpc");
    }
}

public class MemoryPackRpcSerializerEnvelopeTests
{
    private static MemoryPackRpcSerializer NewSerializer() => new();

    [Test]
    public async Task Serialize_Deserialize_RpcRequest_Roundtrip()
    {
        var sut = NewSerializer();
        var request = new RpcRequest
        {
            RequestId = "req-1",
            InterfaceName = "IFoo",
            MethodName = "Bar",
            Arguments =
            [
                new RpcArgument { TypeName = "System.Int32", Payload = new byte[] { 1, 2, 3 } }
            ]
        };
        var bytes = sut.Serialize(request);
        var roundtrip = sut.Deserialize<RpcRequest>(bytes);
        await Assert.That(roundtrip).IsNotNull();
        await Assert.That(roundtrip!.RequestId).IsEqualTo("req-1");
        await Assert.That(roundtrip.InterfaceName).IsEqualTo("IFoo");
        await Assert.That(roundtrip.MethodName).IsEqualTo("Bar");
        await Assert.That(roundtrip.Arguments.Count).IsEqualTo(1);
        await Assert.That(roundtrip.Arguments[0].TypeName).IsEqualTo("System.Int32");
        await Assert.That(roundtrip.Arguments[0].Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcRequest_EmptyArguments_Roundtrip()
    {
        var sut = NewSerializer();
        var request = new RpcRequest { RequestId = "r", InterfaceName = "IX", MethodName = "M" };
        var roundtrip = sut.Deserialize<RpcRequest>(sut.Serialize(request));
        await Assert.That(roundtrip!.Arguments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_Success_WithResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse
        {
            RequestId = "req-1",
            Success = true,
            ResultTypeName = "System.Int32",
            Result = new byte[] { 9, 8, 7 },
            Error = null
        };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.RequestId).IsEqualTo("req-1");
        await Assert.That(roundtrip.Success).IsTrue();
        await Assert.That(roundtrip.ResultTypeName).IsEqualTo("System.Int32");
        await Assert.That(roundtrip.Result!.Value.Span.SequenceEqual(new byte[] { 9, 8, 7 })).IsTrue();
        await Assert.That(roundtrip.Error).IsNull();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_NullResult_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse { RequestId = "r", Success = true, Result = null };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Result.HasValue).IsFalse();
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_Failure_WithError_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse
        {
            RequestId = "req-1",
            Success = false,
            Result = null,
            Error = new RpcError
            {
                Code = "E001",
                Message = "Boom",
                Details = "detail",
                ExceptionType = "System.Exception"
            }
        };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Success).IsFalse();
        await Assert.That(roundtrip.Result.HasValue).IsFalse();
        await Assert.That(roundtrip.Error).IsNotNull();
        await Assert.That(roundtrip.Error!.Code).IsEqualTo("E001");
        await Assert.That(roundtrip.Error.Message).IsEqualTo("Boom");
        await Assert.That(roundtrip.Error.Details).IsEqualTo("detail");
        await Assert.That(roundtrip.Error.ExceptionType).IsEqualTo("System.Exception");
    }

    [Test]
    public async Task Serialize_Deserialize_RpcResponse_Error_NullOptionalFields_Roundtrip()
    {
        var sut = NewSerializer();
        var response = new RpcResponse
        {
            RequestId = "r",
            Success = false,
            Error = new RpcError { Code = "ERR", Message = "msg", Details = null, ExceptionType = null }
        };
        var roundtrip = sut.Deserialize<RpcResponse>(sut.Serialize(response));
        await Assert.That(roundtrip!.Error!.Details).IsNull();
        await Assert.That(roundtrip.Error.ExceptionType).IsNull();
    }
}
```

- [ ] **Step 2: Run envelope tests — expect FAIL**

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "*/*/MemoryPackRpcSerializerEnvelopeTests/*"
```

Expected: All 5 envelope tests fail with `NotImplementedException`.

- [ ] **Step 3: Implement `Formatters/RpcContractFormatters.cs`**

Replace the file with the full implementation:

```csharp
using System.Buffers;
using AsbtCore.Broker.Core;
using MemoryPack;

namespace AsbtCore.Broker.Serialization.MemoryPack.Formatters;

internal sealed class RpcErrorFormatter : IMemoryPackFormatter<RpcError>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcError? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        if (value is null) { writer.WriteNullObjectHeader(); return; }
        writer.WriteObjectHeader(4);
        writer.WriteString(value.Code);
        writer.WriteString(value.Message);
        writer.WriteString(value.Details);
        writer.WriteString(value.ExceptionType);
    }

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcError? value)
    {
        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
        var code = reader.ReadString();
        var message = reader.ReadString();
        var details = reader.ReadString();
        var exceptionType = reader.ReadString();
        value = new RpcError { Code = code!, Message = message!, Details = details, ExceptionType = exceptionType };
    }
}

internal sealed class RpcArgumentFormatter : IMemoryPackFormatter<RpcArgument>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcArgument? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        if (value is null) { writer.WriteNullObjectHeader(); return; }
        writer.WriteObjectHeader(2);
        writer.WriteString(value.TypeName);
        byte[]? bytes = value.Payload.ToArray();
        MemoryPackFormatterProvider.GetFormatter<byte[]>().Serialize(ref writer, ref bytes);
    }

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcArgument? value)
    {
        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
        var typeName = reader.ReadString();
        byte[]? bytes = null;
        MemoryPackFormatterProvider.GetFormatter<byte[]>().Deserialize(ref reader, ref bytes);
        value = new RpcArgument { TypeName = typeName!, Payload = bytes ?? Array.Empty<byte>() };
    }
}

internal sealed class RpcRequestFormatter : IMemoryPackFormatter<RpcRequest>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcRequest? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        if (value is null) { writer.WriteNullObjectHeader(); return; }
        writer.WriteObjectHeader(4);
        writer.WriteString(value.RequestId);
        writer.WriteString(value.InterfaceName);
        writer.WriteString(value.MethodName);
        List<RpcArgument>? args = value.Arguments;
        MemoryPackFormatterProvider.GetFormatter<List<RpcArgument>>().Serialize(ref writer, ref args);
    }

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcRequest? value)
    {
        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
        var requestId = reader.ReadString();
        var interfaceName = reader.ReadString();
        var methodName = reader.ReadString();
        List<RpcArgument>? args = null;
        MemoryPackFormatterProvider.GetFormatter<List<RpcArgument>>().Deserialize(ref reader, ref args);
        value = new RpcRequest
        {
            RequestId = requestId!,
            InterfaceName = interfaceName!,
            MethodName = methodName!,
            Arguments = args ?? []
        };
    }
}

internal sealed class RpcResponseFormatter : IMemoryPackFormatter<RpcResponse>
{
    public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref RpcResponse? value)
        where TBufferWriter : IBufferWriter<byte>
    {
        if (value is null) { writer.WriteNullObjectHeader(); return; }
        writer.WriteObjectHeader(5);
        writer.WriteString(value.RequestId);
        writer.WriteUnmanaged(value.Success);
        writer.WriteString(value.ResultTypeName);
        byte[]? resultBytes = value.Result?.ToArray();
        MemoryPackFormatterProvider.GetFormatter<byte[]>().Serialize(ref writer, ref resultBytes);
        RpcError? error = value.Error;
        MemoryPackFormatterProvider.GetFormatter<RpcError>().Serialize(ref writer, ref error);
    }

    public void Deserialize(ref MemoryPackReader reader, scoped ref RpcResponse? value)
    {
        if (!reader.TryReadObjectHeader(out _)) { value = null; return; }
        var requestId = reader.ReadString();
        reader.ReadUnmanaged(out bool success);
        var resultTypeName = reader.ReadString();
        byte[]? resultBytes = null;
        MemoryPackFormatterProvider.GetFormatter<byte[]>().Deserialize(ref reader, ref resultBytes);
        RpcError? error = null;
        MemoryPackFormatterProvider.GetFormatter<RpcError>().Deserialize(ref reader, ref error);
        value = new RpcResponse
        {
            RequestId = requestId!,
            Success = success,
            ResultTypeName = resultTypeName,
            Result = resultBytes,
            Error = error
        };
    }
}
```

- [ ] **Step 4: Run envelope tests — expect PASS**

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "*/*/MemoryPackRpcSerializerEnvelopeTests/*"
```

Expected: 5 passed, 0 failed.

---

### Task 4: TDD — fragment serialization

**Files:**
- Modify: `Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/MemoryPackRpcSerializerTests.cs`

The fragment path uses `MemoryPackSerializer.Serialize(type, value)` / `Deserialize(type, span)`. These are built-in non-generic MemoryPack APIs — no additional formatter code needed.

- [ ] **Step 1: Add fragment test class to `MemoryPackRpcSerializerTests.cs`**

Append to the end of the file:

```csharp
[MemoryPackable]
public partial record TestFragmentDto(int X, string Label);

public class MemoryPackRpcSerializerFragmentTests
{
    private static MemoryPackRpcSerializer NewSerializer() => new();

    [Test]
    public async Task Fragment_Roundtrips_Int()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(42, typeof(int));
        var value = (int)sut.DeserializeFragment(bytes, typeof(int))!;
        await Assert.That(value).IsEqualTo(42);
    }

    [Test]
    public async Task Fragment_Roundtrips_String()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment("hello", typeof(string));
        var value = (string)sut.DeserializeFragment(bytes, typeof(string))!;
        await Assert.That(value).IsEqualTo("hello");
    }

    [Test]
    public async Task Fragment_Roundtrips_Bool()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(true, typeof(bool));
        var value = (bool)sut.DeserializeFragment(bytes, typeof(bool))!;
        await Assert.That(value).IsTrue();
    }

    [Test]
    public async Task Fragment_Roundtrips_MemoryPackable_Dto()
    {
        var sut = NewSerializer();
        var dto = new TestFragmentDto(7, "hello");
        var bytes = sut.SerializeFragment(dto, typeof(TestFragmentDto));
        var value = (TestFragmentDto)sut.DeserializeFragment(bytes, typeof(TestFragmentDto))!;
        await Assert.That(value.X).IsEqualTo(7);
        await Assert.That(value.Label).IsEqualTo("hello");
    }

    [Test]
    public async Task Fragment_Roundtrips_NullString()
    {
        var sut = NewSerializer();
        var bytes = sut.SerializeFragment(null, typeof(string));
        var value = sut.DeserializeFragment(bytes, typeof(string));
        await Assert.That(value).IsNull();
    }
}
```

- [ ] **Step 2: Run fragment tests — expect PASS** (implementation is already in `MemoryPackRpcSerializer`, no new code)

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "*/*/MemoryPackRpcSerializerFragmentTests/*"
```

Expected: 5 passed, 0 failed.

- [ ] **Step 3: Run all serializer tests**

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj
```

Expected: 13+ passed, 0 failed (ContentType + 6 envelope + 5 fragment + 2 DI = 14 total).

---

### Task 5: Verify DI tests pass

The DI extension is already implemented in Task 1 (skeleton was already complete). Verify:

- [ ] **Step 1: Run DI tests**

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj -- --treenode-filter "*/*/UseMemoryPackRpcSerializationTests/*"
```

Expected: 2 passed (ServerBuilder and ClientBuilder each register `MemoryPackRpcSerializer` as `IRpcSerializer`).

- [ ] **Step 2: Run complete test suite**

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj
```

Expected: 14 passed, 0 failed.

---

### Task 6: Commit adapter + tests

- [ ] **Step 1: Stage and commit**

```bash
git add AsbtCore.Broker.Serialization.MemoryPack/ Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/ RabbitMq.RPC.sln
git commit -m "feat: add MemoryPack serialization adapter and tests"
```

---

### Task 7: Migrate Test.Contracts

**Files:**
- Delete: `Test.Contracts/GeneratorTouchSites.cs`
- Modify: `Test.Contracts/IUserService.cs`
- Modify: `Test.Contracts/Contracts.csproj`

- [ ] **Step 1: Delete `GeneratorTouchSites.cs`**

```bash
rm Test.Contracts/GeneratorTouchSites.cs
```

- [ ] **Step 2: Replace `Test.Contracts/Contracts.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MemoryPack" Version="*" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Replace `Test.Contracts/IUserService.cs`**

Add `[MemoryPackable]` and `partial` to `UserDto`:

```csharp
using MemoryPack;

namespace AsbtCore.Broker.Demo.Contracts
{
    [MemoryPackable]
    public sealed partial class UserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public interface IUserService
    {
        Task<UserDto> GetByIdAsync(int id);
        Task<int> SumAsync(int a, int b);
        Task PingAsync();
    }
}
```

- [ ] **Step 4: Build Contracts to verify**

```bash
dotnet build Test.Contracts/Contracts.csproj
```

Expected: Build succeeded (MemoryPack source generator emits `UserDto`'s generated partial members).

---

### Task 8: Migrate Test.Broker.API

**Files:**
- Modify: `Test.Broker.API/Broker.API.csproj`
- Modify: `Test.Broker.API/Program.cs`

- [ ] **Step 1: Replace `Test.Broker.API/Broker.API.csproj`**

Remove the XPacketRpc reference and add the MemoryPack adapter:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Asp.Versioning.Mvc.ApiExplorer" Version="8.0.0" />
    <PackageReference Include="AspNetCoreRateLimit" Version="5.0.0" />
    <PackageReference Include="JsonDocumentPath" Version="1.0.3" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />

    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
    <PackageReference Include="Swashbuckle.AspNetCore.Annotations" Version="6.5.0" />
    <PackageReference Include="Swashbuckle.AspNetCore.SwaggerUI" Version="6.5.0" />

    <PackageReference Include="Serilog.AspNetCore" Version="8.0.1" />
    <PackageReference Include="Serilog.Enrichers.Environment" Version="2.3.0" />
    <PackageReference Include="Serilog.Exceptions" Version="8.4.0" />

    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.3" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="8.0.3" />
  </ItemGroup>

  <ItemGroup>
    <Folder Include="Controllers\" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AsbtCore.Broker.Core\AsbtCore.Broker.Core.csproj" />
    <ProjectReference Include="..\AsbtCore.Broker.Server\AsbtCore.Broker.Server.csproj" />
    <!--
      The MemoryPack adapter exposes UseMemoryPackRpcSerialization() for both RpcServerBuilder and
      RpcClientBuilder. PrivateAssets="all" pins Client/Server inside the adapter package; consumers
      must add explicit references to resolve both builder types.
    -->
    <ProjectReference Include="..\AsbtCore.Broker.Client\AsbtCore.Broker.Client.csproj" />
    <ProjectReference Include="..\AsbtCore.Broker.Serialization.MemoryPack\AsbtCore.Broker.Serialization.MemoryPack.csproj" />
    <ProjectReference Include="..\Test.Contracts\Contracts.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Update `Test.Broker.API/Program.cs`**

Replace the using directive and the extension call:

- Line 3: change `using AsbtCore.Broker.Serialization.XPacketRpc;` → `using AsbtCore.Broker.Serialization.MemoryPack;`
- Line 59: change `.UseXPacketRpcSerialization()` → `.UseMemoryPackRpcSerialization()`

Result for the relevant lines:

```csharp
using AsbtCore.Broker.Serialization.MemoryPack;
// ... (rest of usings unchanged) ...

builder.Services.AddRabbitRpcServer(builder.Configuration)
    .UseMemoryPackRpcSerialization()
    .Register<IUserService, UserService>(ServiceLifetime.Scoped);
```

- [ ] **Step 3: Build API**

```bash
dotnet build Test.Broker.API/Broker.API.csproj
```

Expected: Build succeeded.

---

### Task 9: Migrate Test.Client

**Files:**
- Modify: `Test.Client/Client.csproj`
- Modify: `Test.Client/Program.cs`

- [ ] **Step 1: Replace `Test.Client/Client.csproj`**

Remove the XPacketRpc reference and add the MemoryPack adapter:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <None Remove="appsettings.json" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />

    <PackageReference Include="Serilog" Version="4.3.1" />
    <PackageReference Include="Serilog.Enrichers.Environment" Version="3.0.1" />
    <PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Serilog.Settings.Configuration" Version="8.0.1" />
    <PackageReference Include="Serilog.Exceptions" Version="8.4.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
    <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
    <PackageReference Include="Serilog.Enrichers.Thread" Version="4.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\AsbtCore.Broker.Client\AsbtCore.Broker.Client.csproj" />
    <ProjectReference Include="..\AsbtCore.Broker.RabbitMq\AsbtCore.Broker.RabbitMq.csproj" />
    <!--
      The MemoryPack adapter exposes UseMemoryPackRpcSerialization() for both RpcClientBuilder and
      RpcServerBuilder. PrivateAssets="all" pins Client/Server inside the adapter package; consumers
      must add explicit references to resolve both builder types.
    -->
    <ProjectReference Include="..\AsbtCore.Broker.Server\AsbtCore.Broker.Server.csproj" />
    <ProjectReference Include="..\AsbtCore.Broker.Serialization.MemoryPack\AsbtCore.Broker.Serialization.MemoryPack.csproj" />
    <ProjectReference Include="..\Test.Contracts\Contracts.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Update `Test.Client/Program.cs`**

Replace the using directive and the extension call:

- Line 3: change `using AsbtCore.Broker.Serialization.XPacketRpc;` → `using AsbtCore.Broker.Serialization.MemoryPack;`
- Line 26: change `.UseXPacketRpcSerialization()` → `.UseMemoryPackRpcSerialization()`

Result for the relevant lines:

```csharp
using AsbtCore.Broker.Serialization.MemoryPack;
// ... (rest of usings unchanged) ...

builder.Services
    .AddRabbitRpcClient(builder.Configuration)
    .UseMemoryPackRpcSerialization()
    .AddProxy<IUserService>();
```

- [ ] **Step 3: Build Client**

```bash
dotnet build Test.Client/Client.csproj
```

Expected: Build succeeded.

---

### Task 10: Full solution build + commit demo migration

- [ ] **Step 1: Build full solution**

```bash
dotnet build RabbitMq.RPC.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run MemoryPack adapter tests one final time**

```bash
dotnet run --project Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests/AsbtCore.Broker.Serialization.MemoryPack.Tests.csproj
```

Expected: 14 passed, 0 failed.

- [ ] **Step 3: Run existing test suites to verify no regressions**

```bash
dotnet run --project Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj
dotnet run --project Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj
```

Expected: All existing tests pass.

- [ ] **Step 4: Commit demo migration**

```bash
git add Test.Contracts/ Test.Broker.API/ Test.Client/
git commit -m "feat(demo): migrate from XPacketRpc to MemoryPack serialization"
```
