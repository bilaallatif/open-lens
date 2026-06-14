# Local Development: .NET Aspire Orchestration

## Context

Local development previously required several manual steps: starting
`docker-compose.yml` (Azurite + Service Bus emulator + SQL Edge), then running
each .NET service separately, with no unified view of logs or traces. MongoDB was
missing from compose entirely, and there was no OpenTelemetry instrumentation.

A [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) **AppHost** now models
the whole distributed app as a single entry point. One command launches every
service and its dependencies together, with a dashboard, structured logs, and
OpenTelemetry wired in.

---

## Running it

Prerequisites:

- .NET 10 SDK
- Docker running (the AppHost starts Azurite, MongoDB, and the Service Bus
  emulator as containers)
- Aspire CLI (`dotnet tool install -g Aspire.Cli`)

From the repo root:

```bash
aspire run
```

or, without the CLI:

```bash
dotnet run --project AppHost
```

The dashboard is served at **https://localhost:17111** — it lists every resource,
its health, console logs, structured logs, traces, and environment. First run
pulls container images, so it takes a few minutes. Stop everything with
`aspire stop` (or Ctrl+C in the foreground).

---

## What the AppHost models

Defined in `AppHost/AppHost.cs`:

| Resource | Aspire declaration | Backs |
|----------|--------------------|-------|
| Blob storage (Azurite) | `AddAzureStorage("storage").RunAsEmulator()` → `AddBlobs("blobs")` | uploaded images |
| Service Bus (emulator) | `AddAzureServiceBus("servicebus").RunAsEmulator()` → `AddServiceBusQueue("image-uploaded")` | the processing trigger |
| MongoDB | `AddMongoDB("mongo").WithDataVolume()` → `AddDatabase("open-lens")` | image metadata |
| `uploadservice` | `AddProject<Projects.UploadService_Api>` | receives uploads, writes blobs |
| `processingservice` | `AddProject<Projects.ProcessingService_Worker>` | consumes the queue, reads blobs, writes metadata |
| `searchservice` | `AddProject<Projects.SearchService_Api>` | queries image metadata |

Each service uses `WaitFor(...)` on its dependencies so it only starts once they
are healthy.

---

## Key design choices

### Connection strings injected into existing config keys

The services bind custom configuration sections (`ObjectStorage`, `Repository`,
`ServiceBus`) via `BindConfiguration(...)` — not the standard `ConnectionStrings:`
section that Aspire client integrations expect. Rather than refactor the services,
the AppHost injects each resource's connection string into the **existing** keys
using the `WithEnvironment(name, resource)` overload:

```csharp
builder.AddProject<Projects.ProcessingService_Worker>("processingservice")
    .WaitFor(imageUploadedQueue)
    .WaitFor(storage)
    .WaitFor(imageMetadataDb)
    .WithEnvironment("ServiceBus__ConnectionString", serviceBus)
    .WithEnvironment("ObjectStorage__ConnectionString", blobs)
    .WithEnvironment("Repository__ConnectionString", imageMetadataDb);
```

This keeps the wiring to a single concern in the AppHost and required **no change**
to how the services read configuration. The non-secret parts of those sections
(`ContainerName`, `DatabaseName`, `QueueName`) stay in each service's
`appsettings.json`.

The injected keys map as follows:

| Service | Config key | Resource |
|---------|------------|----------|
| `uploadservice` | `ObjectStorage:ConnectionString` | `blobs` |
| `processingservice` | `ObjectStorage:ConnectionString` | `blobs` |
| `processingservice` | `ServiceBus:ConnectionString` | `servicebus` |
| `processingservice` | `Repository:ConnectionString` | `open-lens` |
| `searchservice` | `Repository:ConnectionString` | `open-lens` |

### Service Bus emulator over standalone containers

The AppHost uses Aspire's `AddAzureServiceBus(...).RunAsEmulator()` rather than
modelling the old `servicebus-emulator` + `sqledge` containers from
`docker-compose.yml` directly. Aspire provisions the emulator **and** its required
SQL Server sidecar automatically, and declares the `image-uploaded` queue as part
of the resource graph — so the manual SQL dependency wiring disappears.

### Health checks and telemetry centralized in ServiceDefaults

`ServiceDefaults/` is a shared project referenced by all three services. Each calls
`builder.AddServiceDefaults()` in `Program.cs`, opting into OpenTelemetry (traces,
metrics, logs exported to the dashboard via OTLP), default health checks (a `"self"`
liveness check), service discovery, and HTTP resilience.

Health checks live **only** in ServiceDefaults. The services no longer call
`AddHealthChecks()` (it is covered by `AddServiceDefaults()`) or map `/health`
themselves. Instead each calls `app.MapDefaultEndpoints()`, which maps the health
endpoints centrally:

- `/health` — all checks must pass (readiness)
- `/alive` — only checks tagged `live` must pass (liveness)

Note: `MapDefaultEndpoints()` maps these endpoints **only in the Development
environment**, by design — exposing health checks publicly in other environments
has security implications (see https://aka.ms/aspire/healthchecks). If a deployed
environment needs `/health` exposed (e.g. for a container orchestrator probe), relax
that guard in `ServiceDefaults/Extensions.cs`.

### MongoDB has a data volume

`AddMongoDB("mongo").WithDataVolume()` persists Mongo data across runs, so image
metadata survives an `aspire stop` / `aspire run` cycle.

---

## Known limitations

### Service Bus emulator can crash on first start

The `servicebus-emulator:2.0.0` image intermittently exits with code 139 (SIGSEGV)
shortly after its SQL sidecar comes up — an image-level instability, not a
configuration problem. When it happens the `processingservice` stays in `Waiting`
because its `WaitFor(image-uploaded)` gate never clears.

Recovery: restart the `servicebus` resource (the **Restart** command in the
dashboard, or via the Aspire CLI/MCP). It comes back healthy and the worker starts.

### `docker-compose.yml` is still present

Compose is intentionally kept until the Aspire setup is fully validated in regular
use. It is no longer the recommended local-dev path — prefer `aspire run`.

### Out of scope

Production deployment (e.g. Azure Container Apps) is not modelled here; this AppHost
optimizes local development only. The planned McpServer and Angular frontend are not
yet wired — they will be added as `AddProject` / `AddNpmApp` resources when they
exist.
