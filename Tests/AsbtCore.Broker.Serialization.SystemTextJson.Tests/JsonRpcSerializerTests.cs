using System.Text.Json;
using System.Text.Json.Serialization;
using AsbtCore.Broker.Serialization.SystemTextJson;

namespace AsbtCore.Broker.Serialization.SystemTextJson.Tests;

public class RpcJsonOptionsTests
{
    [Test]
    public async Task Options_UseCamelCase()
    {
        await Assert.That(RpcJson.Options.PropertyNamingPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task Options_IgnoreNullsOnWrite()
    {
        await Assert.That(RpcJson.Options.DefaultIgnoreCondition).IsEqualTo(JsonIgnoreCondition.WhenWritingNull);
    }

    [Test]
    public async Task Options_AreCaseInsensitive()
    {
        await Assert.That(RpcJson.Options.PropertyNameCaseInsensitive).IsTrue();
    }
}
