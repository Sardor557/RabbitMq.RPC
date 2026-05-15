using AsbtCore.Broker.Demo.Contracts;

namespace Contracts
{
    public interface ITestDtoService
    {
        Task<TestDto> EchoAsync(TestDto payload);
        Task<int> CountItemsAsync(TestDto payload);
        Task<TestDto> GetItemsAsync(int cnt);
    }
}
