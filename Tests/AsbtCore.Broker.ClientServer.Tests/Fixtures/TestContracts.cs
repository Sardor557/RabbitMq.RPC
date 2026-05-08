using System;
using System.Threading;
using System.Threading.Tasks;

namespace AsbtCore.Broker.ClientServer.Tests.Fixtures
{
    public interface ITestService
    {
        Task<int> AddAsync(int a, int b);
        Task NotifyAsync(string message);
        Task<UserDto> GetUserAsync(Guid id);
    }

    public interface ICancellableService
    {
        Task DoAsync(CancellationToken cancellationToken);
    }

    public interface ISyncService
    {
        int Add(int a, int b);
    }

    public sealed record UserDto(Guid Id, string Name);

    public sealed class TestServiceImpl : ITestService
    {
        public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);
        public Task NotifyAsync(string message) => Task.CompletedTask;
        public Task<UserDto> GetUserAsync(Guid id) => Task.FromResult(new UserDto(id, "U"));
    }

    public sealed class ThrowingServiceImpl : ITestService
    {
        public Task<int> AddAsync(int a, int b) => throw new InvalidOperationException("boom");
        public Task NotifyAsync(string message) => Task.CompletedTask;
        public Task<UserDto> GetUserAsync(Guid id) => Task.FromResult(new UserDto(id, "U"));
    }

    public interface IThrowingService
    {
        Task FailAsync();
    }

    public sealed class ThrowingService : IThrowingService
    {
        public Task FailAsync() => throw new InvalidOperationException("user fail");
    }
}
