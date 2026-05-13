# RabbitRpc.Serialization.MemoryPack

MemoryPack binary serialization adapter for RabbitRpc.

## Usage

Server:
  services.AddRabbitRpcServer(configuration)
      .UseMemoryPackRpcSerialization()
      .Register<IMyService, MyServiceImpl>();

Client:
  services.AddRabbitRpcClient(configuration)
      .UseMemoryPackRpcSerialization()
      .AddRpcProxy<IMyService>();

## DTO Requirements

DTOs must be decorated with [MemoryPackable] and declared partial:

  [MemoryPackable]
  public sealed partial class MyDto
  {
      public int Id { get; set; }
      public string Name { get; set; } = string.Empty;
  }
