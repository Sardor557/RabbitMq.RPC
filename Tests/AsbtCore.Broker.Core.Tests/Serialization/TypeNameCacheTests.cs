using System;
using System.Threading.Tasks;
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
        var assemblyName = typeof(TypeNameCacheTests).Assembly.GetName().Name;

        Assert.ThrowsException<TypeLoadException>(
            () => TypeNameCache.Resolve($"Some.Nonexistent.Type, {assemblyName}"));
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
