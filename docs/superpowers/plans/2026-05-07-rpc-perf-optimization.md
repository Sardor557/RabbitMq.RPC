# RPC Performance Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate hot-path allocations and runtime reflection from the RabbitMq.RPC client/server pipeline by introducing single-shot `JsonElement` serialization, cached compiled invokers, and a type cache. Drop the publish lock. Fix three critical bugs.

**Architecture:** Two new internal static helpers in Core (`RpcSerializationHelper`, `TypeNameCache`). Per-`MethodInfo` compiled-delegate caches on both ends (`RpcClientInvokerCache`, `RpcServerMethodInvoker`). All built with `Expression.Lambda(...).Compile()`. JIT-only (AOT/trimming out of scope).

**Tech Stack:** net10.0, C# 12, `<Nullable>enable</Nullable>`, System.Text.Json, RabbitMQ.Client 7.x, Microsoft.Extensions.* 10.x, MSTest 3.6, BenchmarkDotNet (latest).

**Spec:** [`docs/superpowers/specs/2026-05-07-rpc-perf-optimization-design.md`](../specs/2026-05-07-rpc-perf-optimization-design.md)

**Working directory for all commands:** `C:/Works/RabbitMq.RPC`

---

## Phase 1 — Framework migration (net8 → net10, nullable enable)

### Task 1: Bump TargetFramework and Nullable across all projects

**Files:**
- Modify: `AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj`
- Modify: `AsbtCore.Broker.RabbitMq/AsbtCore.Broker.RabbitMq.csproj`
- Modify: `AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj`
- Modify: `AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj`
- Modify: `Test.Broker.API/Broker.API.csproj`
- Modify: `Test.Client/Client.csproj`
- Modify: `Test.Contracts/Contracts.csproj`
- Modify: `Tests/AsbtCore.Broker.Core.Tests/AsbtCore.Broker.Core.Tests.csproj`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/AsbtCore.Broker.ClientServer.Tests.csproj`

- [ ] **Step 1.1: Edit every csproj — replace `<TargetFramework>net8.0</TargetFramework>` with `<TargetFramework>net10.0</TargetFramework>`**

- [ ] **Step 1.2: Edit every csproj — replace `<Nullable>disable</Nullable>` with `<Nullable>enable</Nullable>`**

- [ ] **Step 1.3: Bump `Microsoft.Extensions.*` package versions to `10.0.0` in every csproj where they appear**

`PackageReference` patterns to update:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
```

`RabbitMQ.Client` stays on `7.1.2`. Test SDK packages (`Microsoft.NET.Test.Sdk`, `MSTest.*`, `Moq`) stay on existing versions.

- [ ] **Step 1.4: Build solution**

```
dotnet build C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln
```

Expected: build may produce nullable warnings (existing code is null-oblivious). Do NOT fix nullable warnings yet — they will be addressed file-by-file as each component is touched in later tasks. Build must succeed (warnings allowed). If build fails (errors, not warnings), fix only the breaking issues (e.g. obsolete API replacements due to .NET 10 changes).

- [ ] **Step 1.5: Run tests to confirm baseline**

```
dotnet test C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln
```

Expected: all existing tests pass on net10.0. Record pass count.

- [ ] **Step 1.6: Commit**

```
git add -A
git commit -m "chore: bump TargetFramework to net10.0 and enable nullable across solution"
```

---

## Phase 2 — Core helpers (TDD)

### Task 2: `RpcSerializationHelper`

**Files:**
- Create: `AsbtCore.Broker.Core/Serialization/RpcSerializationHelper.cs`
- Create: `Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationHelperTests.cs`

- [ ] **Step 2.1: Add `InternalsVisibleTo` for tests in Core csproj**

Edit `AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj` — add inside an `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="AsbtCore.Broker.Core.Tests" />
  <InternalsVisibleTo Include="AsbtCore.Broker.ClientServer.Tests" />
  <InternalsVisibleTo Include="AsbtCore.Broker.Client" />
  <InternalsVisibleTo Include="AsbtCore.Broker.Server" />
  <InternalsVisibleTo Include="AsbtCore.Broker.RabbitMq" />
</ItemGroup>
```

- [ ] **Step 2.2: Write failing test file**

`Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationHelperTests.cs`:

```csharp
using System.Text.Json;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Serialization;

[TestClass]
public sealed class RpcSerializationHelperTests
{
    private sealed record SampleDto(int Id, string Name, double[] Values);

    [TestMethod]
    public void ToElement_PrimitiveInt_ReturnsNumberElement()
    {
        var element = RpcSerializationHelper.ToElement(42, typeof(int));

        Assert.AreEqual(JsonValueKind.Number, element.ValueKind);
        Assert.AreEqual(42, element.GetInt32());
    }

    [TestMethod]
    public void ToElement_NullValue_ReturnsNullElement()
    {
        var element = RpcSerializationHelper.ToElement(null, typeof(string));

        Assert.AreEqual(JsonValueKind.Null, element.ValueKind);
    }

    [TestMethod]
    public void ToElement_Dto_RoundTripsWithFromElement()
    {
        var input = new SampleDto(7, "x", new[] { 1.5, 2.5 });

        var element = RpcSerializationHelper.ToElement(input, typeof(SampleDto));
        var restored = (SampleDto?)RpcSerializationHelper.FromElement(element, typeof(SampleDto));

        Assert.IsNotNull(restored);
        Assert.AreEqual(input, restored);
    }

    [TestMethod]
    public void FromElement_NullJsonElement_ReturnsNullForReferenceType()
    {
        var element = RpcSerializationHelper.ToElement(null, typeof(string));

        var restored = RpcSerializationHelper.FromElement(element, typeof(string));

        Assert.IsNull(restored);
    }
}
```

- [ ] **Step 2.3: Run tests — expect compile failure**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.Core.Tests --filter "FullyQualifiedName~RpcSerializationHelperTests"
```

Expected: build error CS0234 — `RpcSerializationHelper` not found.

- [ ] **Step 2.4: Create the helper**

`AsbtCore.Broker.Core/Serialization/RpcSerializationHelper.cs`:

```csharp
using System.Text.Json;

namespace AsbtCore.Broker.Core.Serialization;

internal static class RpcSerializationHelper
{
    public static JsonElement ToElement(object? value, Type type)
        => JsonSerializer.SerializeToElement(value, type, RpcJson.Options);

    public static object? FromElement(JsonElement element, Type type)
        => element.Deserialize(type, RpcJson.Options);
}
```

- [ ] **Step 2.5: Run tests — expect pass**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.Core.Tests --filter "FullyQualifiedName~RpcSerializationHelperTests"
```

Expected: 4 tests pass.

- [ ] **Step 2.6: Commit**

```
git add AsbtCore.Broker.Core/Serialization/RpcSerializationHelper.cs Tests/AsbtCore.Broker.Core.Tests/Serialization/RpcSerializationHelperTests.cs AsbtCore.Broker.Core/AsbtCore.Broker.Core.csproj
git commit -m "feat(core): add RpcSerializationHelper for single-allocation JsonElement round-trip"
```

---

### Task 3: `TypeNameCache`

**Files:**
- Create: `AsbtCore.Broker.Core/Serialization/TypeNameCache.cs`
- Create: `Tests/AsbtCore.Broker.Core.Tests/Serialization/TypeNameCacheTests.cs`

- [ ] **Step 3.1: Write failing tests**

`Tests/AsbtCore.Broker.Core.Tests/Serialization/TypeNameCacheTests.cs`:

```csharp
using AsbtCore.Broker.Core.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Serialization;

[TestClass]
public sealed class TypeNameCacheTests
{
    [TestMethod]
    public void Resolve_KnownType_ReturnsType()
    {
        var aqn = typeof(int).AssemblyQualifiedName!;

        var resolved = TypeNameCache.Resolve(aqn);

        Assert.AreEqual(typeof(int), resolved);
    }

    [TestMethod]
    public void Resolve_SameNameTwice_ReturnsSameInstance()
    {
        var aqn = typeof(string).AssemblyQualifiedName!;

        var first = TypeNameCache.Resolve(aqn);
        var second = TypeNameCache.Resolve(aqn);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Resolve_UnknownType_Throws()
    {
        Assert.ThrowsException<TypeLoadException>(
            () => TypeNameCache.Resolve("Some.Nonexistent.Type, Nonexistent.Assembly"));
    }

    [TestMethod]
    public void Resolve_ConcurrentCalls_ResolveCorrectly()
    {
        var aqn = typeof(Guid).AssemblyQualifiedName!;

        Parallel.For(0, 1000, _ =>
        {
            var t = TypeNameCache.Resolve(aqn);
            Assert.AreEqual(typeof(Guid), t);
        });
    }
}
```

- [ ] **Step 3.2: Run tests — expect compile failure**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.Core.Tests --filter "FullyQualifiedName~TypeNameCacheTests"
```

Expected: CS0234 — `TypeNameCache` not found.

- [ ] **Step 3.3: Create the cache**

`AsbtCore.Broker.Core/Serialization/TypeNameCache.cs`:

```csharp
using System.Collections.Concurrent;

namespace AsbtCore.Broker.Core.Serialization;

internal static class TypeNameCache
{
    private static readonly ConcurrentDictionary<string, Type> cache = new(StringComparer.Ordinal);

    public static Type Resolve(string typeName)
        => cache.GetOrAdd(typeName, static n => Type.GetType(n, throwOnError: true)!);
}
```

- [ ] **Step 3.4: Run tests — expect pass**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.Core.Tests --filter "FullyQualifiedName~TypeNameCacheTests"
```

Expected: 4 tests pass.

- [ ] **Step 3.5: Commit**

```
git add AsbtCore.Broker.Core/Serialization/TypeNameCache.cs Tests/AsbtCore.Broker.Core.Tests/Serialization/TypeNameCacheTests.cs
git commit -m "feat(core): add TypeNameCache for cached AssemblyQualifiedName resolution"
```

---

## Phase 3 — Server-side delegate cache and dispatcher refactor

### Task 4: `RpcServerMethodInvoker` (delegate factory)

**Files:**
- Create: `AsbtCore.Broker.Server/RpcServerMethodInvoker.cs`
- Create: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerMethodInvokerTests.cs`

- [ ] **Step 4.1: Write failing tests**

`Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerMethodInvokerTests.cs`:

```csharp
using AsbtCore.Broker.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Server;

[TestClass]
public sealed class RpcServerMethodInvokerTests
{
    public sealed class Sample
    {
        public int Add(int a, int b) => a + b;

        public Task PingAsync() => Task.CompletedTask;

        public Task<int> SumAsync(int a, int b) => Task.FromResult(a + b);

        public async Task<string> EchoAsync(string s)
        {
            await Task.Yield();
            return s;
        }

        public Task<int> ThrowAsync()
            => throw new InvalidOperationException("boom");
    }

    [TestMethod]
    public async Task Build_SyncMethod_InvokesAndReturnsResult()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.Add))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), new object?[] { 3, 4 });

        Assert.AreEqual(7, result);
    }

    [TestMethod]
    public async Task Build_TaskMethod_ReturnsNull()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.PingAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), Array.Empty<object?>());

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Build_TaskOfTMethod_ReturnsValue()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.SumAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), new object?[] { 10, 20 });

        Assert.AreEqual(30, result);
    }

    [TestMethod]
    public async Task Build_AsyncTaskOfT_ReturnsValue()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.EchoAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        var result = await invoker(new Sample(), new object?[] { "hi" });

        Assert.AreEqual("hi", result);
    }

    [TestMethod]
    public async Task Build_ThrowingMethod_PropagatesException()
    {
        var method = typeof(Sample).GetMethod(nameof(Sample.ThrowAsync))!;
        var invoker = RpcServerMethodInvoker.Build(method);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            async () => await invoker(new Sample(), Array.Empty<object?>()));
    }
}
```

- [ ] **Step 4.2: Run tests — expect compile failure**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.ClientServer.Tests --filter "FullyQualifiedName~RpcServerMethodInvokerTests"
```

Expected: CS0234 — `RpcServerMethodInvoker` not found.

- [ ] **Step 4.3: Implement `RpcServerMethodInvoker`**

`AsbtCore.Broker.Server/RpcServerMethodInvoker.cs`:

```csharp
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace AsbtCore.Broker.Server;

internal delegate Task<object?> RpcMethodInvocation(object instance, object?[] args);

internal static class RpcServerMethodInvoker
{
    private static readonly ConcurrentDictionary<Type, Func<object, Task<object?>>> taskAdaptorCache = new();

    public static RpcMethodInvocation Build(MethodInfo method)
    {
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var argsParam = Expression.Parameter(typeof(object?[]), "args");

        var parameters = method.GetParameters();
        var argExpressions = new Expression[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var arrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
            argExpressions[i] = Expression.Convert(arrayAccess, parameters[i].ParameterType);
        }

        Expression instanceExpr = method.IsStatic
            ? null!
            : Expression.Convert(instanceParam, method.DeclaringType!);

        Expression call = Expression.Call(instanceExpr, method, argExpressions);

        var returnType = method.ReturnType;

        Expression body;

        if (returnType == typeof(void))
        {
            body = Expression.Block(
                call,
                Expression.Constant(Task.FromResult<object?>(null), typeof(Task<object?>)));
        }
        else if (returnType == typeof(Task))
        {
            var nonGenericAdaptor = typeof(RpcServerMethodInvoker)
                .GetMethod(nameof(WrapNonGenericTask), BindingFlags.NonPublic | BindingFlags.Static)!;
            body = Expression.Call(nonGenericAdaptor, call);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var genericAdaptor = typeof(RpcServerMethodInvoker)
                .GetMethod(nameof(WrapGenericTask), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(resultType);
            body = Expression.Call(genericAdaptor, call);
        }
        else
        {
            // Sync non-task return
            var castResult = Expression.Convert(call, typeof(object));
            var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(typeof(object?));
            body = Expression.Call(fromResult, castResult);
        }

        var lambda = Expression.Lambda<RpcMethodInvocation>(body, instanceParam, argsParam);
        return lambda.Compile();
    }

    private static async Task<object?> WrapNonGenericTask(Task task)
    {
        await task.ConfigureAwait(false);
        return null;
    }

    private static async Task<object?> WrapGenericTask<T>(Task<T> task)
    {
        var result = await task.ConfigureAwait(false);
        return result;
    }
}
```

- [ ] **Step 4.4: Run tests — expect pass**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.ClientServer.Tests --filter "FullyQualifiedName~RpcServerMethodInvokerTests"
```

Expected: 5 tests pass.

- [ ] **Step 4.5: Commit**

```
git add AsbtCore.Broker.Server/RpcServerMethodInvoker.cs Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerMethodInvokerTests.cs
git commit -m "feat(server): add RpcServerMethodInvoker compiled delegate factory"
```

---

### Task 5: `RpcMethodEntry` and `RpcServerDescriptor` refactor

**Files:**
- Modify: `AsbtCore.Broker.Server/RpcServerDescriptor.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcServerRegistryTests.cs` (if it asserts old `TryGetMethod` shape)

- [ ] **Step 5.1: Update `RpcServerDescriptor.cs` to new shape**

Replace contents of `AsbtCore.Broker.Server/RpcServerDescriptor.cs`:

```csharp
using System.Reflection;

namespace AsbtCore.Broker.Server;

internal sealed record RpcMethodEntry(MethodInfo Method, RpcMethodInvocation Invoker);

public sealed class RpcServerDescriptor
{
    private readonly Dictionary<string, RpcMethodEntry> methods;

    public Type InterfaceType { get; }
    public Type ImplementationType { get; }
    public string InterfaceName { get; }
    public string Route { get; }

    internal RpcServerDescriptor(
        Type interfaceType,
        Type implementationType,
        string route,
        Dictionary<string, RpcMethodEntry> methods)
    {
        InterfaceType = interfaceType;
        ImplementationType = implementationType;
        InterfaceName = interfaceType.FullName
            ?? throw new InvalidOperationException($"Interface {interfaceType} has no FullName.");
        Route = route;
        this.methods = methods;
    }

    internal bool TryGetMethod(string methodName, IReadOnlyList<string> parameterTypeNames, out RpcMethodEntry entry)
    {
        var key = BuildMethodKey(methodName, parameterTypeNames);
        return methods.TryGetValue(key, out entry!);
    }

    public static string BuildMethodKey(string methodName, IEnumerable<string> parameterTypeNames)
        => $"{methodName}|{string.Join(";", parameterTypeNames)}";
}
```

Note: `RpcMethodEntry` is `internal`. `TryGetMethod` becomes `internal`. Public surface keeps `BuildMethodKey` and properties; the descriptor itself stays public for DI consumers but its method bag is internal — acceptable break for v2.

- [ ] **Step 5.2: Build solution**

```
dotnet build C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln
```

Expected: build fails — `RpcServerRegistry.BuildMethodMap` returns `Dictionary<string, MethodInfo>`, not `Dictionary<string, RpcMethodEntry>`. Will fix in Task 6.

- [ ] **Step 5.3: Commit (broken build is OK — will fix immediately in next task)**

```
git add AsbtCore.Broker.Server/RpcServerDescriptor.cs
git commit -m "refactor(server): RpcServerDescriptor stores RpcMethodEntry with cached invoker"
```

---

### Task 6: `RpcServerRegistry.BuildMethodMap` updates

**Files:**
- Modify: `AsbtCore.Broker.Server/RpcServerRegistry.cs`

- [ ] **Step 6.1: Replace contents of `RpcServerRegistry.cs`**

```csharp
using System.Reflection;
using AsbtCore.Broker.Core.Abstractions;

namespace AsbtCore.Broker.Server;

public sealed class RpcServerRegistry
{
    private readonly Dictionary<string, RpcServerDescriptor> map;

    public RpcServerRegistry(
        IEnumerable<RpcServerRegistration> registrations,
        IRpcRouteResolver routeResolver)
    {
        map = new Dictionary<string, RpcServerDescriptor>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            var interfaceType = registration.InterfaceType;
            var implementationType = registration.ImplementationType;

            var methods = BuildMethodMap(interfaceType, implementationType);
            var route = registration.ExplicitRoute ?? routeResolver.Resolve(interfaceType);

            var descriptor = new RpcServerDescriptor(
                interfaceType,
                implementationType,
                route,
                methods);

            map[descriptor.InterfaceName] = descriptor;
        }
    }

    public bool TryGet(string interfaceName, out RpcServerDescriptor descriptor)
        => map.TryGetValue(interfaceName, out descriptor!);

    public IReadOnlyCollection<string> GetRoutes()
        => map.Values
            .Select(x => x.Route)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, RpcMethodEntry> BuildMethodMap(Type interfaceType, Type implementationType)
    {
        var result = new Dictionary<string, RpcMethodEntry>(StringComparer.Ordinal);
        var map = implementationType.GetInterfaceMap(interfaceType);

        for (int i = 0; i < map.InterfaceMethods.Length; i++)
        {
            var interfaceMethod = map.InterfaceMethods[i];
            var targetMethod = map.TargetMethods[i];

            var parameterTypeNames = interfaceMethod
                .GetParameters()
                .Select(p => p.ParameterType.AssemblyQualifiedName
                             ?? p.ParameterType.FullName
                             ?? throw new InvalidOperationException($"Cannot get type name for {p.ParameterType}."))
                .ToArray();

            var key = RpcServerDescriptor.BuildMethodKey(interfaceMethod.Name, parameterTypeNames);
            var invoker = RpcServerMethodInvoker.Build(targetMethod);
            result[key] = new RpcMethodEntry(targetMethod, invoker);
        }

        return result;
    }
}
```

- [ ] **Step 6.2: Build solution**

```
dotnet build C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln
```

Expected: build fails inside `RpcRequestDispatcher.cs` (still uses old `out MethodInfo`). Will fix in Task 7.

- [ ] **Step 6.3: Commit**

```
git add AsbtCore.Broker.Server/RpcServerRegistry.cs
git commit -m "refactor(server): build RpcMethodEntry with compiled invoker per interface method"
```

---

### Task 7: `RpcRequestDispatcher` refactor (use cache + helper)

**Files:**
- Modify: `AsbtCore.Broker.Server/RpcRequestDispatcher.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcRequestDispatcherTests.cs`

- [ ] **Step 7.1: Replace contents of `RpcRequestDispatcher.cs`**

```csharp
using System.Reflection;
using System.Text.Json;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace AsbtCore.Broker.Server;

public sealed class RpcRequestDispatcher
{
    private readonly RpcServerRegistry registry;
    private readonly IServiceScopeFactory scopeFactory;

    public RpcRequestDispatcher(RpcServerRegistry registry, IServiceScopeFactory scopeFactory)
    {
        this.registry = registry;
        this.scopeFactory = scopeFactory;
    }

    public async Task<RpcResponse> DispatchAsync(RpcRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!registry.TryGet(request.InterfaceName, out var descriptor))
            {
                return CreateError(request.RequestId, "service_not_found",
                    $"Service '{request.InterfaceName}' not found.");
            }

            var parameterTypeNames = request.Arguments.Select(x => x.TypeName).ToArray();

            if (!descriptor.TryGetMethod(request.MethodName, parameterTypeNames, out var entry))
            {
                return CreateError(request.RequestId, "method_not_found",
                    $"Method '{request.MethodName}' with specified signature was not found.");
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService(descriptor.ImplementationType);

            var args = new object?[request.Arguments.Count];
            for (int i = 0; i < request.Arguments.Count; i++)
            {
                var arg = request.Arguments[i];
                Type type;
                try
                {
                    type = TypeNameCache.Resolve(arg.TypeName);
                }
                catch (Exception ex)
                {
                    return CreateError(request.RequestId, "type_not_found",
                        $"Type '{arg.TypeName}' (argument {i}) could not be resolved.", ex);
                }

                try
                {
                    args[i] = RpcSerializationHelper.FromElement(arg.Payload, type);
                }
                catch (Exception ex)
                {
                    return CreateError(request.RequestId, "deserialization_error",
                        $"Failed to deserialize argument {i} of method '{request.MethodName}'.", ex);
                }
            }

            object? result;
            try
            {
                result = await entry.Invoker(service, args);
            }
            catch (TargetInvocationException ex)
            {
                var real = ex.InnerException ?? ex;
                return CreateError(request.RequestId, "invocation_error", real.Message, real);
            }

            var logicalResultType = GetLogicalResultType(entry.Method.ReturnType);

            return new RpcResponse
            {
                RequestId = request.RequestId,
                Success = true,
                ResultTypeName = logicalResultType?.AssemblyQualifiedName ?? logicalResultType?.FullName,
                Result = logicalResultType is null ? null : RpcSerializationHelper.ToElement(result, logicalResultType)
            };
        }
        catch (Exception ex)
        {
            return CreateError(request.RequestId, "server_error", ex.Message, ex);
        }
    }

    private static Type? GetLogicalResultType(Type returnType)
    {
        if (returnType == typeof(void) || returnType == typeof(Task))
            return null;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return returnType.GetGenericArguments()[0];

        return returnType;
    }

    private static RpcResponse CreateError(string requestId, string code, string message, Exception? exception = null)
        => new()
        {
            RequestId = requestId,
            Success = false,
            Error = new RpcError
            {
                Code = code,
                Message = message,
                Details = exception?.ToString(),
                ExceptionType = exception?.GetType().FullName
            }
        };
}
```

- [ ] **Step 7.2: Build solution**

```
dotnet build C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln
```

Expected: build succeeds. Existing dispatcher tests may fail — that's the next step.

- [ ] **Step 7.3: Open existing `RpcRequestDispatcherTests.cs`. Read its current asserts. Delete any test that asserts the legacy `JsonDocument.Parse`/byte-array intermediate or that exercises `MethodInfo.Invoke` directly. Update behavior tests to reflect new error codes. Add new tests:**

Append to `Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcRequestDispatcherTests.cs`:

```csharp
[TestMethod]
public async Task DispatchAsync_UnknownArgumentType_ReturnsTypeNotFoundError()
{
    // Arrange dispatcher per existing helper. Replace BogusServiceFixture with the
    // existing test fixture used in this file. The argument has an unresolvable
    // AssemblyQualifiedName.
    var dispatcher = TestDispatcherFactory.Create<IUserService, UserService>();
    var request = new RpcRequest
    {
        InterfaceName = typeof(IUserService).FullName!,
        MethodName = nameof(IUserService.SumAsync),
        Arguments =
        {
            new RpcArgument
            {
                TypeName = "Bogus.Type, Bogus.Assembly",
                Payload = JsonSerializer.SerializeToElement(1)
            }
        }
    };

    var response = await dispatcher.DispatchAsync(request);

    Assert.IsFalse(response.Success);
    Assert.AreEqual("type_not_found", response.Error!.Code);
}

[TestMethod]
public async Task DispatchAsync_MalformedArgumentPayload_ReturnsDeserializationError()
{
    var dispatcher = TestDispatcherFactory.Create<IUserService, UserService>();
    var request = new RpcRequest
    {
        InterfaceName = typeof(IUserService).FullName!,
        MethodName = nameof(IUserService.SumAsync),
        Arguments =
        {
            new RpcArgument
            {
                TypeName = typeof(int).AssemblyQualifiedName!,
                Payload = JsonSerializer.SerializeToElement("not an int")
            },
            new RpcArgument
            {
                TypeName = typeof(int).AssemblyQualifiedName!,
                Payload = JsonSerializer.SerializeToElement(2)
            }
        }
    };

    var response = await dispatcher.DispatchAsync(request);

    Assert.IsFalse(response.Success);
    Assert.AreEqual("deserialization_error", response.Error!.Code);
}
```

If `TestDispatcherFactory` doesn't yet exist, create it from the existing arrange logic in the file (factor it out). The existing fixtures `IUserService`/`UserService` should already be in `Fixtures/TestContracts.cs`.

- [ ] **Step 7.4: Run all dispatcher tests**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.ClientServer.Tests --filter "FullyQualifiedName~RpcRequestDispatcherTests"
```

Expected: all pass.

- [ ] **Step 7.5: Commit**

```
git add AsbtCore.Broker.Server/RpcRequestDispatcher.cs Tests/AsbtCore.Broker.ClientServer.Tests/Server/RpcRequestDispatcherTests.cs
git commit -m "refactor(server): RpcRequestDispatcher uses cached invokers and helpers"
```

---

## Phase 4 — Client-side delegate cache and RpcClient refactor

### Task 8: `RpcClientInvokerCache`

**Files:**
- Create: `AsbtCore.Broker.Client/RpcClientInvokerCache.cs`
- Create: `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientInvokerCacheTests.cs`

- [ ] **Step 8.1: Write failing tests**

`Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientInvokerCacheTests.cs`:

```csharp
using System.Reflection;
using AsbtCore.Broker.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.ClientServer.Tests.Client;

[TestClass]
public sealed class RpcClientInvokerCacheTests
{
    public interface ISample
    {
        Task PingAsync();
        Task<int> SumAsync(int a, int b);
    }

    [TestMethod]
    public void Get_TaskMethod_ReturnsDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.PingAsync))!;

        var del = RpcClientInvokerCache.Get(method);

        Assert.IsNotNull(del);
    }

    [TestMethod]
    public void Get_TaskOfTMethod_ReturnsDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;

        var del = RpcClientInvokerCache.Get(method);

        Assert.IsNotNull(del);
    }

    [TestMethod]
    public void Get_SameMethodTwice_ReturnsSameDelegate()
    {
        var method = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;

        var first = RpcClientInvokerCache.Get(method);
        var second = RpcClientInvokerCache.Get(method);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Get_NonTaskReturn_Throws()
    {
        var method = typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

        Assert.ThrowsException<NotSupportedException>(() => RpcClientInvokerCache.Get(method));
    }
}
```

- [ ] **Step 8.2: Run tests — expect compile failure**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.ClientServer.Tests --filter "FullyQualifiedName~RpcClientInvokerCacheTests"
```

Expected: CS0234 — `RpcClientInvokerCache` not found.

- [ ] **Step 8.3: Implement cache**

`AsbtCore.Broker.Client/RpcClientInvokerCache.cs`:

```csharp
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace AsbtCore.Broker.Client;

internal delegate object RpcClientInvocation(
    RpcClient client,
    Type interfaceType,
    object[] args,
    TimeSpan? timeout,
    CancellationToken cancellationToken);

internal static class RpcClientInvokerCache
{
    private static readonly ConcurrentDictionary<MethodInfo, RpcClientInvocation> cache = new();

    public static RpcClientInvocation Get(MethodInfo method)
        => cache.GetOrAdd(method, BuildInvocation);

    private static RpcClientInvocation BuildInvocation(MethodInfo method)
    {
        var returnType = method.ReturnType;

        if (returnType == typeof(Task))
            return BuildVoidInvocation(method);

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return BuildGenericInvocation(method, returnType.GetGenericArguments()[0]);

        throw new NotSupportedException(
            $"Remote method '{method.Name}' must return Task or Task<T>; got {returnType}.");
    }

    private static RpcClientInvocation BuildVoidInvocation(MethodInfo method)
    {
        var voidAsync = typeof(RpcClient)
            .GetMethod("InvokeVoidAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return BuildLambda(method, voidAsync);
    }

    private static RpcClientInvocation BuildGenericInvocation(MethodInfo method, Type resultType)
    {
        var genericAsync = typeof(RpcClient)
            .GetMethod("InvokeGenericAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType);
        return BuildLambda(method, genericAsync);
    }

    private static RpcClientInvocation BuildLambda(MethodInfo interfaceMethod, MethodInfo target)
    {
        var clientParam = Expression.Parameter(typeof(RpcClient), "client");
        var interfaceTypeParam = Expression.Parameter(typeof(Type), "interfaceType");
        var argsParam = Expression.Parameter(typeof(object[]), "args");
        var timeoutParam = Expression.Parameter(typeof(TimeSpan?), "timeout");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var call = Expression.Call(
            clientParam,
            target,
            interfaceTypeParam,
            Expression.Constant(interfaceMethod, typeof(MethodInfo)),
            argsParam,
            timeoutParam,
            ctParam);

        var body = Expression.Convert(call, typeof(object));
        return Expression.Lambda<RpcClientInvocation>(body,
            clientParam, interfaceTypeParam, argsParam, timeoutParam, ctParam).Compile();
    }
}
```

- [ ] **Step 8.4: Run tests — expect pass**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.ClientServer.Tests --filter "FullyQualifiedName~RpcClientInvokerCacheTests"
```

Expected: 4 tests pass.

- [ ] **Step 8.5: Commit**

```
git add AsbtCore.Broker.Client/RpcClientInvokerCache.cs Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcClientInvokerCacheTests.cs
git commit -m "feat(client): add RpcClientInvokerCache compiled-delegate cache for proxy dispatch"
```

---

### Task 9: `RpcClient.InvokeProxy` and `BuildRequest` refactor

**Files:**
- Modify: `AsbtCore.Broker.Client/RpcClient.cs`

- [ ] **Step 9.1: Replace contents of `RpcClient.cs`**

```csharp
using System.Reflection;
using System.Text.Json;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Exceptions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Client;

public sealed class RpcClient
{
    private readonly IRpcTransport transport;
    private readonly IRpcRouteResolver routeResolver;
    private readonly RpcOptions options;

    public RpcClient(
        IRpcTransport transport,
        IRpcRouteResolver routeResolver,
        IRpcSerializer serializer,
        IOptions<RpcOptions> options)
    {
        this.transport = transport;
        this.routeResolver = routeResolver;
        this.options = options.Value;
    }

    internal object InvokeProxy(Type interfaceType, MethodInfo targetMethod, object[] args, TimeSpan? timeout = null)
    {
        var invocation = RpcClientInvokerCache.Get(targetMethod);
        return invocation(this, interfaceType, args, timeout, CancellationToken.None);
    }

    private Task InvokeVoidAsync(Type interfaceType, MethodInfo method,
        object[] args, TimeSpan? timeout, CancellationToken cancellationToken)
        => SendAsync<object>(interfaceType, method, args, timeout, expectsResult: false, cancellationToken);

    private Task<T?> InvokeGenericAsync<T>(Type interfaceType, MethodInfo method,
        object[] args, TimeSpan? timeout, CancellationToken cancellationToken)
        => SendAsync<T>(interfaceType, method, args, timeout, expectsResult: true, cancellationToken);

    private async Task<T?> SendAsync<T>(
        Type interfaceType,
        MethodInfo method,
        object[] args,
        TimeSpan? timeout,
        bool expectsResult,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(interfaceType, method, args);
        var route = routeResolver.Resolve(interfaceType);

        var response = await transport.SendAsync(
            request,
            route,
            timeout ?? TimeSpan.FromSeconds(options.DefaultTimeoutSeconds),
            cancellationToken);

        if (!response.Success)
        {
            throw new RpcRemoteException(
                response.Error?.Message ?? "Remote call failed.",
                response.Error?.Code,
                response.Error?.ExceptionType,
                response.Error?.Details);
        }

        if (!expectsResult)
            return default;

        if (response.Result is null || response.Result.Value.ValueKind == JsonValueKind.Undefined)
            return default;

        return response.Result.Value.Deserialize<T>(RpcJson.Options);
    }

    private static RpcRequest BuildRequest(Type interfaceType, MethodInfo method, object[] args)
    {
        var interfaceName = interfaceType.FullName
            ?? throw new InvalidOperationException($"Type {interfaceType} has no FullName.");

        var parameters = method.GetParameters();
        args ??= Array.Empty<object>();

        if (parameters.Length != args.Length)
        {
            throw new InvalidOperationException(
                $"Argument count mismatch for method '{method.Name}'. Expected {parameters.Length}, got {args.Length}.");
        }

        var request = new RpcRequest
        {
            InterfaceName = interfaceName,
            MethodName = method.Name
        };

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;

            if (parameterType == typeof(CancellationToken))
            {
                throw new NotSupportedException(
                    $"Method '{method.Name}' contains CancellationToken parameter. Use timeout on transport/client level.");
            }

            var typeName = parameterType.AssemblyQualifiedName
                ?? parameterType.FullName
                ?? throw new InvalidOperationException($"Cannot resolve type name for {parameterType}.");

            request.Arguments.Add(new RpcArgument
            {
                TypeName = typeName,
                Payload = RpcSerializationHelper.ToElement(args[i], parameterType)
            });
        }

        return request;
    }
}
```

- [ ] **Step 9.2: Build solution**

```
dotnet build C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln
```

Expected: build succeeds.

- [ ] **Step 9.3: Run all client tests**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.ClientServer.Tests --filter "FullyQualifiedName~Client"
```

Expected: existing client tests pass. Some may fail if they asserted byte-array intermediates — fix those by adjusting expectations to `JsonElement`-only.

- [ ] **Step 9.4: Commit**

```
git add AsbtCore.Broker.Client/RpcClient.cs
git commit -m "refactor(client): RpcClient uses cached invoker and SerializeToElement"
```

---

## Phase 5 — Transport fixes

### Task 10: Drop `publishLock` from `RabbitMqRpcTransport`

**Files:**
- Modify: `AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs`
- Modify: `Tests/AsbtCore.Broker.Core.Tests/Transport/RabbitMqRpcTransportTests.cs`

- [ ] **Step 10.1: Replace contents of `RabbitMqRpcTransport.cs`**

```csharp
using System.Collections.Concurrent;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AsbtCore.Broker.RabbitMq.Transport;

public sealed class RabbitMqRpcTransport : IRpcTransport, IAsyncDisposable, IDisposable
{
    private readonly IRabbitMqConnectionProvider connectionProvider;
    private readonly ILogger<RabbitMqRpcTransport> logger;
    private readonly IRpcSerializer serializer;

    private readonly SemaphoreSlim initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> pending = new();

    private IChannel? publishChannel;
    private IChannel? replyChannel;
    private string? replyQueueName;
    private bool initialized;

    public RabbitMqRpcTransport(
        IRabbitMqConnectionProvider connectionProvider,
        ILogger<RabbitMqRpcTransport> logger,
        IRpcSerializer serializer)
    {
        this.connectionProvider = connectionProvider;
        this.logger = logger;
        this.serializer = serializer;
    }

    public async Task<RpcResponse> SendAsync(
        RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (publishChannel is null || replyQueueName is null)
            throw new InvalidOperationException("Transport is not initialized.");

        var properties = new BasicProperties
        {
            CorrelationId = request.RequestId,
            ReplyTo = replyQueueName,
            ContentType = serializer.ContentType
        };

        var body = serializer.Serialize(request);

        var tcs = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!pending.TryAdd(request.RequestId, tcs))
            throw new InvalidOperationException($"Duplicate request id '{request.RequestId}'.");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue)
            linkedCts.CancelAfter(timeout.Value);

        await using var registration = linkedCts.Token.Register(
            static state =>
            {
                var (tcs, token) = ((TaskCompletionSource<RpcResponse>, CancellationToken))state!;
                tcs.TrySetCanceled(token);
            },
            (tcs, linkedCts.Token));

        try
        {
            await publishChannel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: route,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: linkedCts.Token);

            logger.LogDebug(
                "RPC request published. RequestId: {RequestId}, Route: {Route}, Method: {Method}",
                request.RequestId,
                route,
                request.MethodName);

            return await tcs.Task;
        }
        finally
        {
            pending.TryRemove(request.RequestId, out _);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        await initLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
                return;

            var connection = await connectionProvider.GetConnectionAsync(cancellationToken);

            publishChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            replyChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            var declareOk = await replyChannel.QueueDeclareAsync(
                queue: string.Empty,
                durable: false,
                exclusive: true,
                autoDelete: true,
                arguments: null,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken);

            replyQueueName = declareOk.QueueName;

            var consumer = new AsyncEventingBasicConsumer(replyChannel);
            consumer.ReceivedAsync += OnResponseReceivedAsync;

            await replyChannel.BasicConsumeAsync(
                queue: replyQueueName,
                autoAck: true,
                consumer: consumer,
                cancellationToken: cancellationToken);

            initialized = true;

            logger.LogInformation("RabbitMQ RPC transport initialized. Reply queue: {ReplyQueue}", replyQueueName);
        }
        finally
        {
            initLock.Release();
        }
    }

    private Task OnResponseReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        try
        {
            var correlationId = ea.BasicProperties.CorrelationId;

            if (string.IsNullOrWhiteSpace(correlationId))
                return Task.CompletedTask;

            var response = serializer.Deserialize<RpcResponse>(ea.Body);

            if (response is null)
                return Task.CompletedTask;

            if (pending.TryRemove(correlationId, out var tcs))
                tcs.TrySetResult(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while handling RPC response.");
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (replyChannel is not null)
            await replyChannel.DisposeAsync();

        if (publishChannel is not null)
            await publishChannel.DisposeAsync();

        initLock.Dispose();
    }

    public void Dispose()
    {
        replyChannel?.Dispose();
        publishChannel?.Dispose();
        initLock.Dispose();
    }
}
```

Changes vs. previous: removed `publishLock` `SemaphoreSlim` and the wait/release block; added `linkedCts.Token.Register` to set the pending TCS as cancelled on timeout/cancel; nullable-aware fields.

- [ ] **Step 10.2: Update transport tests — add concurrency assertion and cancellation assertions**

In `Tests/AsbtCore.Broker.Core.Tests/Transport/RabbitMqRpcTransportTests.cs`, delete any test asserting `publishLock` semantics. Add (or replace) tests:

```csharp
[TestMethod]
public async Task SendAsync_ConcurrentRequests_BothPublishWithoutSerialization()
{
    var (transport, mock) = TestTransportFactory.Create();

    var first = transport.SendAsync(new RpcRequest { RequestId = "1" }, "route");
    var second = transport.SendAsync(new RpcRequest { RequestId = "2" }, "route");

    // Mock channel records BasicPublishAsync invocations; verify both reached publish
    // before either gets a response.
    await mock.WaitForPublishCountAsync(2);
    Assert.AreEqual(2, mock.PublishCount);

    mock.CompletePending("1", new RpcResponse { RequestId = "1", Success = true });
    mock.CompletePending("2", new RpcResponse { RequestId = "2", Success = true });

    await Task.WhenAll(first, second);
}

[TestMethod]
public async Task SendAsync_TimeoutFires_PendingTcsCancelled()
{
    var (transport, mock) = TestTransportFactory.Create();

    var task = transport.SendAsync(new RpcRequest { RequestId = "x" }, "route", TimeSpan.FromMilliseconds(50));

    await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await task);
    Assert.IsFalse(mock.HasPending("x"));
}
```

If `TestTransportFactory` and `mock.WaitForPublishCountAsync`/`CompletePending`/`HasPending` helpers don't exist, factor them out from existing test setup in this file. The helpers wrap a fake `IChannel`, fake `IRabbitMqConnectionProvider`, and the transport's `pending` dictionary access (via existing test seam or reflection).

- [ ] **Step 10.3: Run transport tests**

```
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.Core.Tests --filter "FullyQualifiedName~RabbitMqRpcTransportTests"
```

Expected: all pass.

- [ ] **Step 10.4: Commit**

```
git add AsbtCore.Broker.RabbitMq/Transport/RabbitMqRpcTransport.cs Tests/AsbtCore.Broker.Core.Tests/Transport/RabbitMqRpcTransportTests.cs
git commit -m "refactor(transport): drop publishLock and explicitly cancel pending TCS on timeout"
```

---

## Phase 6 — Bug fixes (DispatchProxy timeout, Test.Client NRE)

### Task 11: `RpcDispatchProxy` timeout from `RpcOptions`

**Files:**
- Modify: `AsbtCore.Broker.Client/RpcProxyFactory.cs`
- Modify: `Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcProxyFactoryTests.cs`

- [ ] **Step 11.1: Replace contents of `RpcProxyFactory.cs`**

```csharp
using System.Reflection;
using AsbtCore.Broker.Core.Options;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Client;

public sealed class RpcProxyFactory
{
    private readonly RpcClient client;
    private readonly TimeSpan defaultTimeout;

    public RpcProxyFactory(RpcClient client, IOptions<RpcOptions> options)
    {
        this.client = client;
        this.defaultTimeout = TimeSpan.FromSeconds(options.Value.DefaultTimeoutSeconds);
    }

    public T CreateProxy<T>() where T : class
    {
        var proxy = DispatchProxy.Create<T, RpcDispatchProxy>();

        if (proxy is not RpcDispatchProxy dispatchProxy)
            throw new InvalidOperationException("Failed to create DispatchProxy.");

        dispatchProxy.Configure(client, typeof(T), defaultTimeout);
        return (T)(object)dispatchProxy;
    }
}

internal sealed class RpcDispatchProxy : DispatchProxy
{
    private RpcClient client = default!;
    private Type interfaceType = default!;
    private TimeSpan timeout;

    public void Configure(RpcClient client, Type interfaceType, TimeSpan timeout)
    {
        this.client = client;
        this.interfaceType = interfaceType;
        this.timeout = timeout;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            throw new ArgumentNullException(nameof(targetMethod));

        return client.InvokeProxy(interfaceType, targetMethod, args ?? Array.Empty<object>()!, timeout);
    }
}
```

- [ ] **Step 11.2: Update existing test — `RpcProxyFactoryTests.cs`**

Locate the existing test that asserts `timeout = 10s` hardcode. Replace with one that wires `IOptions<RpcOptions> { DefaultTimeoutSeconds = 7 }` and asserts the proxy uses `7s`:

```csharp
[TestMethod]
public async Task CreateProxy_UsesDefaultTimeoutSecondsFromOptions()
{
    var transportSpy = new TimeoutCapturingTransport();
    var options = Options.Create(new RpcOptions
    {
        HostName = "h", VirtualHost = "/", UserName = "u", Password = "p",
        ClientProvidedName = "c", Port = 1, DefaultTimeoutSeconds = 7
    });

    var client = new RpcClient(transportSpy, new DefaultRpcRouteResolver(options),
        new JsonRpcSerializer(), options);
    var factory = new RpcProxyFactory(client, options);

    var proxy = factory.CreateProxy<IUserService>();

    await proxy.PingAsync();

    Assert.AreEqual(TimeSpan.FromSeconds(7), transportSpy.LastTimeout);
}
```

`TimeoutCapturingTransport` is a tiny `IRpcTransport` that captures `timeout`. If a similar fixture exists in the test project, reuse it; otherwise create it under `Fixtures/`.

- [ ] **Step 11.3: Build and run tests**

```
dotnet build C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln
dotnet test C:/Works/RabbitMq.RPC/Tests/AsbtCore.Broker.ClientServer.Tests --filter "FullyQualifiedName~RpcProxyFactoryTests"
```

Expected: pass.

- [ ] **Step 11.4: Commit**

```
git add AsbtCore.Broker.Client/RpcProxyFactory.cs Tests/AsbtCore.Broker.ClientServer.Tests/Client/RpcProxyFactoryTests.cs
git commit -m "fix(client): RpcDispatchProxy timeout sourced from RpcOptions.DefaultTimeoutSeconds"
```

---

### Task 12: `Test.Client` NRE fix

**Files:**
- Modify: `Test.Client/Program.cs`

- [ ] **Step 12.1: Find the loop in `Test.Client/Program.cs` containing `b.Trim()`. Replace the loop block with:**

```csharp
while (true)
{
    Console.WriteLine("Enter two numbers to sum (or 'exit' to quit):");

    var a = Console.ReadLine();
    var b = Console.ReadLine();

    if (string.Equals(a?.Trim(), "exit", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(b?.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
        break;

    if (!int.TryParse(a, out var ai) || !int.TryParse(b, out var bi))
    {
        Console.WriteLine("Invalid input — please enter integers.");
        continue;
    }

    var sum = await userService.SumAsync(ai, bi);
    Console.WriteLine($"Sum = {sum}");
}
```

- [ ] **Step 12.2: Build**

```
dotnet build C:/Works/RabbitMq.RPC/Test.Client/Client.csproj
```

Expected: success.

- [ ] **Step 12.3: Commit**

```
git add Test.Client/Program.cs
git commit -m "fix(test-client): null-check stdin and use TryParse to avoid NRE"
```

---

## Phase 7 — NuGet version bump

### Task 13: Bump package versions to 2.0.0

**Files:**
- Modify: `AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj`
- Modify: `AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj`

- [ ] **Step 13.1: In each csproj find `<Version>1.0.2</Version>` (or current value) and replace with `<Version>2.0.0</Version>`.**

- [ ] **Step 13.2: Add release notes to each csproj inside `<PropertyGroup>`:**

```xml
<PackageReleaseNotes>2.0.0 — Performance pass: cached compiled invokers, single-allocation JsonElement, TypeNameCache, no publishLock. Migration: net10.0; Nullable enable. Breaking: internal RpcServerDescriptor methods, RpcDispatchProxy timeout now sourced from RpcOptions.DefaultTimeoutSeconds.</PackageReleaseNotes>
```

- [ ] **Step 13.3: Build packages**

```
dotnet pack C:/Works/RabbitMq.RPC/AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj -c Release
dotnet pack C:/Works/RabbitMq.RPC/AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj -c Release
```

Expected: `.nupkg` files emitted with version `2.0.0`.

- [ ] **Step 13.4: Commit**

```
git add AsbtCore.Broker.Client/AsbtCore.Broker.Client.csproj AsbtCore.Broker.Server/AsbtCore.Broker.Server.csproj
git commit -m "chore: bump RabbitRpc.Client and RabbitRpc.Server to 2.0.0"
```

---

## Phase 8 — Benchmarks

### Task 14: Create `Benchmarks` project skeleton

**Files:**
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/AsbtCore.Broker.Benchmarks.csproj`
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/Program.cs`
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/README.md`

- [ ] **Step 14.1: Create csproj**

`Benchmarks/AsbtCore.Broker.Benchmarks/AsbtCore.Broker.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\AsbtCore.Broker.Core\AsbtCore.Broker.Core.csproj" />
    <ProjectReference Include="..\..\AsbtCore.Broker.Client\AsbtCore.Broker.Client.csproj" />
    <ProjectReference Include="..\..\AsbtCore.Broker.Server\AsbtCore.Broker.Server.csproj" />
    <ProjectReference Include="..\..\AsbtCore.Broker.RabbitMq\AsbtCore.Broker.RabbitMq.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 14.2: Create entry point**

`Benchmarks/AsbtCore.Broker.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
```

- [ ] **Step 14.3: Create README**

`Benchmarks/AsbtCore.Broker.Benchmarks/README.md`:

```markdown
# AsbtCore.Broker Benchmarks

Run all:

    dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter '*'

Run a specific class:

    dotnet run -c Release --project Benchmarks/AsbtCore.Broker.Benchmarks --filter '*JsonElementCreationBench*'

Each per-optimization benchmark exposes a `[Params(LegacyOrNew.Legacy, LegacyOrNew.New)]` switch so before/after numbers come from the same harness.

Acceptance targets are documented in the design spec at
`docs/superpowers/specs/2026-05-07-rpc-perf-optimization-design.md` (Section 8.4).
```

- [ ] **Step 14.4: Add project to solution**

```
dotnet sln C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln add C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks/AsbtCore.Broker.Benchmarks.csproj --solution-folder Benchmarks
```

- [ ] **Step 14.5: Build**

```
dotnet build C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks/AsbtCore.Broker.Benchmarks.csproj -c Release
```

Expected: success.

- [ ] **Step 14.6: Commit**

```
git add Benchmarks/ RabbitMq.RPC.sln
git commit -m "chore(benchmarks): add BenchmarkDotNet project skeleton"
```

---

### Task 15: `JsonElementCreationBench`

**Files:**
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/JsonElementCreationBench.cs`

- [ ] **Step 15.1: Create benchmark**

```csharp
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AsbtCore.Broker.Benchmarks;

public enum LegacyOrNew { Legacy, New }

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class JsonElementCreationBench
{
    public sealed record Small(int Id, string Name);
    public sealed record Nested(int Id, Small Inner, string[] Tags);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private Small smallValue = null!;
    private Nested nestedValue = null!;
    private List<Small> listValue = null!;

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        smallValue = new Small(42, "x");
        nestedValue = new Nested(1, new Small(2, "n"), new[] { "a", "b", "c" });
        listValue = Enumerable.Range(0, 50).Select(i => new Small(i, $"n{i}")).ToList();
    }

    [Benchmark]
    public JsonElement Small_Element() => Run(smallValue, typeof(Small));

    [Benchmark]
    public JsonElement Nested_Element() => Run(nestedValue, typeof(Nested));

    [Benchmark]
    public JsonElement List_Element() => Run(listValue, typeof(List<Small>));

    private JsonElement Run(object value, Type type) => Mode switch
    {
        LegacyOrNew.Legacy => LegacyToElement(value, type),
        LegacyOrNew.New => JsonSerializer.SerializeToElement(value, type, Options),
        _ => throw new InvalidOperationException()
    };

    private static JsonElement LegacyToElement(object value, Type type)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type, Options);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }
}
```

- [ ] **Step 15.2: Build**

```
dotnet build C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks -c Release
```

Expected: success.

- [ ] **Step 15.3: Commit**

```
git add Benchmarks/AsbtCore.Broker.Benchmarks/JsonElementCreationBench.cs
git commit -m "feat(benchmarks): add JsonElementCreationBench (legacy vs new)"
```

---

### Task 16: `RpcClientInvokerBench`

**Files:**
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/RpcClientInvokerBench.cs`

- [ ] **Step 16.1: Create benchmark**

```csharp
using System.Reflection;
using AsbtCore.Broker.Client;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class RpcClientInvokerBench
{
    public interface ISample
    {
        Task PingAsync();
        Task<int> SumAsync(int a, int b);
    }

    private MethodInfo pingMethod = null!;
    private MethodInfo sumMethod = null!;
    private RpcClientInvocation cachedSum = null!;
    private RpcClient client = null!;

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        pingMethod = typeof(ISample).GetMethod(nameof(ISample.PingAsync))!;
        sumMethod = typeof(ISample).GetMethod(nameof(ISample.SumAsync))!;
        client = BenchmarkClientFactory.CreateInProcessClient();
        cachedSum = RpcClientInvokerCache.Get(sumMethod);
    }

    [Benchmark]
    public object SumAsync_Dispatch() => Mode switch
    {
        LegacyOrNew.Legacy => LegacyDispatch(sumMethod, new object[] { 1, 2 }),
        LegacyOrNew.New => cachedSum(client, typeof(ISample), new object[] { 1, 2 }, null, default),
        _ => throw new InvalidOperationException()
    };

    private object LegacyDispatch(MethodInfo method, object[] args)
    {
        // Replicate legacy behaviour: MakeGenericMethod + MethodInfo.Invoke
        var resultType = method.ReturnType.GetGenericArguments()[0];
        var generic = typeof(RpcClient)
            .GetMethod("InvokeGenericAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType);
        return generic.Invoke(client, new object?[] { typeof(ISample), method, args, null, CancellationToken.None })!;
    }
}
```

`BenchmarkClientFactory.CreateInProcessClient()` — helper that wires `RpcClient` against an in-memory `IRpcTransport` returning a fixed `RpcResponse` immediately. Implement next.

- [ ] **Step 16.2: Create `BenchmarkClientFactory.cs`**

`Benchmarks/AsbtCore.Broker.Benchmarks/BenchmarkClientFactory.cs`:

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Benchmarks;

internal static class BenchmarkClientFactory
{
    public static RpcClient CreateInProcessClient()
    {
        var options = Options.Create(new RpcOptions
        {
            HostName = "localhost", VirtualHost = "/", UserName = "u", Password = "p",
            ClientProvidedName = "bench", Port = 5672, DefaultTimeoutSeconds = 30
        });
        var transport = new InProcessTransport();
        var resolver = new DefaultRpcRouteResolver(options);
        var serializer = new JsonRpcSerializer();
        return new RpcClient(transport, resolver, serializer, options);
    }

    private sealed class InProcessTransport : IRpcTransport
    {
        public Task<RpcResponse> SendAsync(
            RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var response = new RpcResponse
            {
                RequestId = request.RequestId,
                Success = true,
                Result = System.Text.Json.JsonSerializer.SerializeToElement(0, RpcJson.Options)
            };
            return Task.FromResult(response);
        }
    }
}
```

- [ ] **Step 16.3: Build**

```
dotnet build C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks -c Release
```

Expected: success.

- [ ] **Step 16.4: Commit**

```
git add Benchmarks/AsbtCore.Broker.Benchmarks/RpcClientInvokerBench.cs Benchmarks/AsbtCore.Broker.Benchmarks/BenchmarkClientFactory.cs
git commit -m "feat(benchmarks): add RpcClientInvokerBench and in-process client factory"
```

---

### Task 17: `RpcServerInvokerBench`

**Files:**
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/RpcServerInvokerBench.cs`

- [ ] **Step 17.1: Create benchmark**

```csharp
using System.Reflection;
using AsbtCore.Broker.Server;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class RpcServerInvokerBench
{
    public sealed class Sample
    {
        public int Add(int a, int b) => a + b;
        public Task PingAsync() => Task.CompletedTask;
        public Task<int> SumAsync(int a, int b) => Task.FromResult(a + b);
    }

    private Sample instance = null!;
    private MethodInfo addMethod = null!;
    private MethodInfo pingMethod = null!;
    private MethodInfo sumMethod = null!;
    private RpcMethodInvocation addInvoker = null!;
    private RpcMethodInvocation pingInvoker = null!;
    private RpcMethodInvocation sumInvoker = null!;

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        instance = new Sample();
        addMethod = typeof(Sample).GetMethod(nameof(Sample.Add))!;
        pingMethod = typeof(Sample).GetMethod(nameof(Sample.PingAsync))!;
        sumMethod = typeof(Sample).GetMethod(nameof(Sample.SumAsync))!;
        addInvoker = RpcServerMethodInvoker.Build(addMethod);
        pingInvoker = RpcServerMethodInvoker.Build(pingMethod);
        sumInvoker = RpcServerMethodInvoker.Build(sumMethod);
    }

    [Benchmark]
    public async Task<object?> SumAsync_Invoke() => Mode switch
    {
        LegacyOrNew.Legacy => await LegacyInvokeAsync(sumMethod, new object?[] { 3, 4 }),
        LegacyOrNew.New => await sumInvoker(instance, new object?[] { 3, 4 }),
        _ => throw new InvalidOperationException()
    };

    [Benchmark]
    public async Task<object?> Add_Invoke() => Mode switch
    {
        LegacyOrNew.Legacy => await LegacyInvokeAsync(addMethod, new object?[] { 3, 4 }),
        LegacyOrNew.New => await addInvoker(instance, new object?[] { 3, 4 }),
        _ => throw new InvalidOperationException()
    };

    [Benchmark]
    public async Task<object?> Ping_Invoke() => Mode switch
    {
        LegacyOrNew.Legacy => await LegacyInvokeAsync(pingMethod, Array.Empty<object?>()),
        LegacyOrNew.New => await pingInvoker(instance, Array.Empty<object?>()),
        _ => throw new InvalidOperationException()
    };

    private async Task<object?> LegacyInvokeAsync(MethodInfo method, object?[] args)
    {
        var result = method.Invoke(instance, args);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return task.GetType().GetProperty("Result")!.GetValue(task);
            }
            return null;
        }
        return result;
    }
}
```

- [ ] **Step 17.2: Build**

```
dotnet build C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks -c Release
```

- [ ] **Step 17.3: Commit**

```
git add Benchmarks/AsbtCore.Broker.Benchmarks/RpcServerInvokerBench.cs
git commit -m "feat(benchmarks): add RpcServerInvokerBench"
```

---

### Task 18: `TypeResolutionBench`

**Files:**
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/TypeResolutionBench.cs`

- [ ] **Step 18.1: Create benchmark**

```csharp
using AsbtCore.Broker.Core.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class TypeResolutionBench
{
    private string aqn = null!;

    [Params(1, 10, 1000)]
    public int Calls { get; set; }

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup() => aqn = typeof(Guid).AssemblyQualifiedName!;

    [Benchmark]
    public Type Resolve()
    {
        Type t = typeof(object);
        for (int i = 0; i < Calls; i++)
        {
            t = Mode switch
            {
                LegacyOrNew.Legacy => Type.GetType(aqn, throwOnError: true)!,
                LegacyOrNew.New => TypeNameCache.Resolve(aqn),
                _ => throw new InvalidOperationException()
            };
        }
        return t;
    }
}
```

- [ ] **Step 18.2: Build + commit**

```
dotnet build C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks -c Release
git add Benchmarks/AsbtCore.Broker.Benchmarks/TypeResolutionBench.cs
git commit -m "feat(benchmarks): add TypeResolutionBench"
```

---

### Task 19: `PublishConcurrencyBench`

**Files:**
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/PublishConcurrencyBench.cs`

- [ ] **Step 19.1: Create benchmark — measures throughput of N parallel `BasicPublishAsync` calls with vs without `SemaphoreSlim`**

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace AsbtCore.Broker.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class PublishConcurrencyBench
{
    private SemaphoreSlim semaphore = null!;

    [Params(1, 4, 16, 64)]
    public int Concurrency { get; set; }

    [Params(LegacyOrNew.Legacy, LegacyOrNew.New)]
    public LegacyOrNew Mode { get; set; }

    [GlobalSetup]
    public void Setup() => semaphore = new SemaphoreSlim(1, 1);

    [Benchmark]
    public async Task ParallelPublish()
    {
        var tasks = new Task[Concurrency];
        for (int i = 0; i < Concurrency; i++)
        {
            tasks[i] = Mode switch
            {
                LegacyOrNew.Legacy => LegacyPublishAsync(),
                LegacyOrNew.New => NewPublishAsync(),
                _ => throw new InvalidOperationException()
            };
        }
        await Task.WhenAll(tasks);
    }

    private async Task LegacyPublishAsync()
    {
        await semaphore.WaitAsync();
        try { await SimulatePublishAsync(); }
        finally { semaphore.Release(); }
    }

    private static Task NewPublishAsync() => SimulatePublishAsync();

    private static async Task SimulatePublishAsync()
    {
        // Simulates the work of BasicPublishAsync: small async hop + fixed CPU cost.
        await Task.Yield();
    }
}
```

- [ ] **Step 19.2: Build + commit**

```
dotnet build C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks -c Release
git add Benchmarks/AsbtCore.Broker.Benchmarks/PublishConcurrencyBench.cs
git commit -m "feat(benchmarks): add PublishConcurrencyBench"
```

---

### Task 20: `RpcRoundTripBench` (end-to-end in-process)

**Files:**
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/RpcRoundTripBench.cs`
- Create: `Benchmarks/AsbtCore.Broker.Benchmarks/InMemoryTransport.cs`

- [ ] **Step 20.1: Create in-memory transport that loops requests through the dispatcher**

`Benchmarks/AsbtCore.Broker.Benchmarks/InMemoryTransport.cs`:

```csharp
using AsbtCore.Broker.Core;
using AsbtCore.Broker.Core.Abstractions;
using AsbtCore.Broker.Server;

namespace AsbtCore.Broker.Benchmarks;

internal sealed class InMemoryTransport : IRpcTransport
{
    private readonly RpcRequestDispatcher dispatcher;

    public InMemoryTransport(RpcRequestDispatcher dispatcher) => this.dispatcher = dispatcher;

    public Task<RpcResponse> SendAsync(
        RpcRequest request, string route, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => dispatcher.DispatchAsync(request, cancellationToken);
}
```

- [ ] **Step 20.2: Create benchmark**

`Benchmarks/AsbtCore.Broker.Benchmarks/RpcRoundTripBench.cs`:

```csharp
using AsbtCore.Broker.Client;
using AsbtCore.Broker.Core.Options;
using AsbtCore.Broker.Core.Routing;
using AsbtCore.Broker.Core.Serialization;
using AsbtCore.Broker.Server;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AsbtCore.Broker.Benchmarks;

public interface IBenchService
{
    Task PingAsync();
    Task<int> SumAsync(int a, int b);
    Task<UserDto> GetByIdAsync(int id);
}

public sealed record UserDto(int Id, string Name);

public sealed class BenchService : IBenchService
{
    public Task PingAsync() => Task.CompletedTask;
    public Task<int> SumAsync(int a, int b) => Task.FromResult(a + b);
    public Task<UserDto> GetByIdAsync(int id) => Task.FromResult(new UserDto(id, $"u{id}"));
}

[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class RpcRoundTripBench
{
    private IBenchService proxy = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BenchService>();
        services.AddSingleton<IBenchService>(sp => sp.GetRequiredService<BenchService>());
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new RpcOptions
        {
            HostName = "h", VirtualHost = "/", UserName = "u", Password = "p",
            ClientProvidedName = "bench", Port = 1, DefaultTimeoutSeconds = 30
        });
        var resolver = new DefaultRpcRouteResolver(options);
        var registry = new RpcServerRegistry(
            new[] { new RpcServerRegistration(typeof(IBenchService), typeof(BenchService)) },
            resolver);
        var dispatcher = new RpcRequestDispatcher(registry, sp.GetRequiredService<IServiceScopeFactory>());
        var transport = new InMemoryTransport(dispatcher);
        var client = new RpcClient(transport, resolver, new JsonRpcSerializer(), options);
        var factory = new RpcProxyFactory(client, options);
        proxy = factory.CreateProxy<IBenchService>();
    }

    [Benchmark]
    public Task PingAsync() => proxy.PingAsync();

    [Benchmark]
    public Task<int> SumAsync() => proxy.SumAsync(2, 3);

    [Benchmark]
    public Task<UserDto> GetByIdAsync() => proxy.GetByIdAsync(7);
}
```

- [ ] **Step 20.3: Build + commit**

```
dotnet build C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks -c Release
git add Benchmarks/AsbtCore.Broker.Benchmarks/RpcRoundTripBench.cs Benchmarks/AsbtCore.Broker.Benchmarks/InMemoryTransport.cs
git commit -m "feat(benchmarks): add RpcRoundTripBench end-to-end harness"
```

---

### Task 21: Run benchmarks and verify acceptance criteria

- [ ] **Step 21.1: Run all benches**

```
dotnet run -c Release --project C:/Works/RabbitMq.RPC/Benchmarks/AsbtCore.Broker.Benchmarks --filter '*'
```

- [ ] **Step 21.2: Compare reported numbers against design spec Section 8.4**

| # | Metric | Target | Actual | Pass? |
|---|--------|--------|--------|-------|
| 1 | JsonElementCreation alloc | ≥ 40 % ↓ | | |
| 1 | JsonElementCreation time | ≥ 25 % ↓ | | |
| 2 | RpcClientInvoker time (post first) | ≥ 70 % ↓ | | |
| 3 | RpcServerInvoker time | ≥ 60 % ↓ | | |
| 4 | TypeResolution time (post first) | ≥ 95 % ↓ | | |
| 5 | PublishConcurrency throughput @16 | ≥ 3× ↑ | | |
| 6 | RoundTrip alloc | ≥ 30 % ↓ | | |
| 6 | RoundTrip time | ≥ 20 % ↓ | | |

Fill in the table from BenchmarkDotNet output.

- [ ] **Step 21.3: If any target is missed, iterate the relevant component (do not declare the work complete). Re-run that benchmark class only to confirm.**

- [ ] **Step 21.4: Once all targets pass, save the BenchmarkDotNet artifacts**

```
mv BenchmarkDotNet.Artifacts Benchmarks/AsbtCore.Broker.Benchmarks/results-2026-05-07/
git add Benchmarks/AsbtCore.Broker.Benchmarks/results-2026-05-07/
git commit -m "chore(benchmarks): record initial perf baseline (acceptance criteria met)"
```

---

## Final verification

### Task 22: Full solution sanity

- [ ] **Step 22.1: Full build**

```
dotnet build C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln -c Release
```

Expected: success, no errors. Warnings allowed but should be triaged (nullable warnings on legacy unmodified files are acceptable for now).

- [ ] **Step 22.2: Full test run**

```
dotnet test C:/Works/RabbitMq.RPC/RabbitMq.RPC.sln -c Release
```

Expected: all tests pass.

- [ ] **Step 22.3: Smoke test against a live RabbitMQ broker if available**

```
dotnet run --project C:/Works/RabbitMq.RPC/Test.Broker.API
# in another terminal
dotnet run --project C:/Works/RabbitMq.RPC/Test.Client
```

Expected: client receives a `UserDto`, ping completes, sum returns correctly. Skip if no broker available.

- [ ] **Step 22.4: Tag the release**

```
git tag v2.0.0
```

(Pushing to remote is up to the user — do not push automatically.)

---

## Out of scope (tracked for follow-up plans)

- Publisher confirms (`CreateChannelOptions(publisherConfirmationsEnabled: true)`).
- Reply-queue topology recovery on connection recovery.
- DLQ / retry-count headers for poison messages.
- AOT / trimming compatibility (Roslyn-source-generator proxy + STJ source generator).
- xUnit v3 migration.
- Secrets management (RabbitMQ credentials currently in `appsettings.json`).
- `Test.Broker.API` Swagger Basic Auth credentials hardcoded in source.
- `IRpcSerializer.ContentType` should be `application/json` (currently `System.Text.Json`).
