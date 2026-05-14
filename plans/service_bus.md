# Plan: Service Bus Consumer in ProcessingService.Worker

## Context

`ProcessingService.Worker` is a long-running ASP.NET Core Worker Service that consumes `ImageUploadedEvent` messages from the `image-uploaded` Azure Service Bus queue. This document records the consumer skeleton that was built — message receive, deserialise, complete/abandon, error handling — without yet wiring the domain work (blob download, EXIF extraction, persistence). That comes in a later slice.

The shape is **`ServiceBusClient` → `ServiceBusProcessor` → event handlers**, hosted inside a `BackgroundService`. The `Azure.Messaging.ServiceBus` SDK is the only Service Bus dependency.

---

## What Was Added

| File | Purpose |
|---|---|
| `ProcessingService.Worker/Models/ImageUploadedEvent.cs` | Message contract — `record(BlobName, ContainerName)`. Inline for now; moves to `Shared.Contracts` when that library exists. |
| `ProcessingService.Worker/Options/ServiceBusOptions.cs` | Strongly-typed config — `ConnectionString` + `QueueName`. |
| `ProcessingService.Worker/Workers/ImageProcessingWorker.cs` | `BackgroundService` that owns the `ServiceBusProcessor` lifecycle. |
| `ProcessingService.Worker/Program.cs` | DI registration: `ServiceBusClient` singleton, options binding, hosted service. |
| `ProcessingService.Worker/appsettings.json` | `ServiceBus.QueueName = "image-uploaded"`. |
| `ProcessingService.Worker/appsettings.Development.json` | Emulator connection string. |
| `docker-compose.yml` | Adds `servicebus-emulator` + `sqledge` services. |
| `servicebus-emulator/Config.json` | Pre-declares the `image-uploaded` queue inside the emulator. |
| `Directory.Packages.props` | Adds `Azure.Messaging.ServiceBus 7.18.2`. |

---

## Consumer Pattern — Reference

### Lifecycle

`BackgroundService.ExecuteAsync` is called once when the host starts. The Service Bus SDK's `ServiceBusProcessor` owns its own internal pump threads — once `StartProcessingAsync` returns, messages are dispatched to your handlers in the background. `ExecuteAsync` itself just needs to stay alive until shutdown, so it parks on `Task.Delay(Timeout.Infinite, stoppingToken)`.

```
ExecuteAsync
  ├── client.CreateProcessor(queueName, options)
  ├── processor.ProcessMessageAsync += HandleMessageAsync
  ├── processor.ProcessErrorAsync   += HandleErrorAsync
  ├── processor.StartProcessingAsync(stoppingToken)
  └── await Task.Delay(Timeout.Infinite, stoppingToken)   // park

StopAsync (called by host on shutdown)
  ├── processor.StopProcessingAsync(cancellationToken)    // drains in-flight messages
  └── processor.DisposeAsync()
```

### Per-Message Flow

Each delivery invokes `ProcessMessageAsync` with a `ProcessMessageEventArgs`. The handler is responsible for explicitly acking or abandoning:

```
HandleMessageAsync(args)
  ├── args.Message.Body.ToObjectFromJson<ImageUploadedEvent>()   // deserialise
  ├── … domain work …
  ├── args.CompleteMessageAsync(args.Message)                    // ack — remove from queue
  └── on exception: args.AbandonMessageAsync(args.Message)       // release lock → retry
```

`AutoCompleteMessages = false` is the safe default: a handler exception otherwise silently drops the message. Explicit complete/abandon makes the contract obvious.

### Error Handler

`ProcessErrorAsync` is a **separate channel** for broker-level problems — connectivity, auth, lock expiry, etc. It is informational only: you cannot ack or abandon from it, you can only log. Per-message exceptions never reach this handler; they surface inside `ProcessMessageAsync`.

### Processor Options

```csharp
new ServiceBusProcessorOptions
{
    AutoCompleteMessages = false,
    MaxConcurrentCalls   = 1
}
```

`MaxConcurrentCalls = 1` keeps the worker single-threaded for the reference implementation — easier to reason about ordering and log output. Bump it later when throughput matters; the SDK handles the parallelism, the handler just needs to be reentrant-safe.

### DI Registration

`ServiceBusClient` is registered as a **singleton** — it owns AMQP connections and is designed to be reused. Each call to `CreateProcessor` produces a lightweight processor scoped to a single queue; the `BackgroundService` creates and disposes its own.

```csharp
builder.Services.AddOptions<ServiceBusOptions>()
    .BindConfiguration("ServiceBus");

builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    return new ServiceBusClient(opts.ConnectionString);
});

builder.Services.AddHostedService<ImageProcessingWorker>();
```

---

## Local Emulator

Microsoft's official Azure Service Bus Emulator runs as a Docker container backed by Azure SQL Edge. Both are declared in `docker-compose.yml` alongside Azurite.

```
docker-compose.yml
  ├── azurite                # Blob Storage emulator (port 10000)
  ├── servicebus-emulator    # SB emulator (AMQP on port 5672, management on 5300)
  └── sqledge                # SQL backend the emulator depends on
```

The emulator pre-declares queues from a JSON config mounted into the container at startup. Queue topology is **not** managed by the consumer — the queue must already exist before the worker connects.

```
servicebus-emulator/Config.json
  └── UserConfig.Namespaces[0].Queues[0].Name = "image-uploaded"
```

### Connection String

When the SDK sees `UseDevelopmentEmulator=true`, it bypasses authentication and the SAS key is a placeholder. The host is always `localhost`.

```
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;
```

Lives in `appsettings.Development.json`. Azure deployments will get a real namespace + managed identity (see [[spec]]: `Azure Service Bus Data Receiver` role for `processing-service`).

---

## Verification

1. **Start the stack**
   ```
   docker compose up -d
   ```
   First run pulls ~1GB (emulator image + sqledge).

2. **Run the worker**
   ```
   dotnet run --project ProcessingService.Worker
   ```
   Expect log line: `Starting Service Bus processor for queue image-uploaded`.

3. **Publish a test message** — from any scratch project referencing `Azure.Messaging.ServiceBus`:
   ```csharp
   var conn = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
   await using var client = new ServiceBusClient(conn);
   var sender = client.CreateSender("image-uploaded");
   await sender.SendMessageAsync(new ServiceBusMessage(
       BinaryData.FromObjectAsJson(new { BlobName = "test.jpg", ContainerName = "images" })));
   ```
   Worker log should show:
   `Received ImageUploadedEvent (MessageId=…): test.jpg in images`

4. **Failure path** — temporarily throw inside `HandleMessageAsync` before `CompleteMessageAsync`. The message should be abandoned and redelivered up to `MaxDeliveryCount` (3, per `Config.json`) before being dead-lettered.

---

## Open Items / Next Slices

- **Wire the handler to the domain flow** — currently `HandleMessageAsync` only logs. Next step is to call `IBlobDownloadService` (not yet defined) → `IMetadataService` → `IRepository<ImageMetadata>`. See [[processing_service]].
- **Move `ImageUploadedEvent` to `Shared.Contracts`** when the shared library is created. See [[shared]].
- **Real Event Grid schema** — in Azure, Event Grid forwards `Microsoft.Storage.BlobCreated` events to the queue, which have a richer payload than the inline contract. The deserialisation step will need to map from that schema (or `upload-service` should normalise before publishing — TBD).
- **Production auth** — managed identity instead of connection string. `ServiceBusClient` has a constructor that takes `(string fullyQualifiedNamespace, TokenCredential credential)`; swap config + DI when deploying to ACA.
