# ASP.NET Core integration testing — gRPC services {#grpc}

Section of `knowledge/csharp/aspnet-integration-testing.md`.


The same factory hosts gRPC: create a channel over the in-memory handler and call the generated
client. See `knowledge/csharp/grpc.md#testing`.

```csharp
var channel = GrpcChannel.ForAddress(factory.Server.BaseAddress,
    new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
var client = new Orders.OrdersClient(channel);
```
