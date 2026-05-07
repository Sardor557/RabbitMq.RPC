using System;
using System.Threading.Tasks;
using AsbtCore.Broker.Core.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsbtCore.Broker.Core.Tests.Serialization;

[TestClass]
public sealed class StableTypeNameTests
{
    [TestMethod]
    public void Resolve_KnownType_ReturnsType()
    {
        var name = StableTypeName.From(typeof(int));

        var resolved = StableTypeName.Resolve(name);

        Assert.AreEqual(typeof(int), resolved);
    }

    [TestMethod]
    public void Resolve_SameNameTwice_ReturnsSameInstance()
    {
        var name = StableTypeName.From(typeof(string));

        var first = StableTypeName.Resolve(name);
        var second = StableTypeName.Resolve(name);

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Resolve_UnknownType_Throws()
    {
        var assemblyName = typeof(StableTypeNameTests).Assembly.GetName().Name;

        Assert.ThrowsException<TypeLoadException>(
            () => StableTypeName.Resolve($"Some.Nonexistent.Type, {assemblyName}"));
    }

    [TestMethod]
    public void Resolve_ConcurrentCalls_ResolveCorrectly()
    {
        var name = StableTypeName.From(typeof(Guid));

        Parallel.For(0, 1000, _ =>
        {
            var t = StableTypeName.Resolve(name);
            Assert.AreEqual(typeof(Guid), t);
        });
    }
}
