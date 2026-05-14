# Plan: ProcessingService — Clean Architecture

## Context

Building the `ProcessingService` from scratch. It is an ASP.NET Core Worker Service that:
1. Consumes `ImageUploadedEvent` messages from Azure Service Bus
2. Downloads the image blob from Azure Blob Storage
3. Extracts EXIF metadata via `MetadataExtractor`
4. Persists structured metadata to MongoDB

Architecture: Clean Architecture (Jason Taylor template) — Domain / Application / Infrastructure / Worker.
CQRS via MediatR, validation via FluentValidation pipeline behaviour.
A .NET Aspire `AppHost` is introduced to orchestrate the Worker + a MongoDB container locally.

---

## Projects to Create

| Project | SDK | Solution folder |
|---|---|---|
| `ProcessingService.Domain` | `Microsoft.NET.Sdk` | `/services/ProcessingService/src/Domain/` |
| `ProcessingService.Application` | `Microsoft.NET.Sdk` | `/services/ProcessingService/src/Application/` |
| `ProcessingService.Infrastructure` | `Microsoft.NET.Sdk` | `/services/ProcessingService/src/Infrastructure/` |
| `ProcessingService.Worker` | `Microsoft.NET.Sdk.Web` | `/services/ProcessingService/src/Worker/` |
| `AppHost` | `Microsoft.NET.Sdk` + `<IsAspireHost>true` | `/services/AppHost/` |

---

## Physical Directory Structure

```
AppHost/
  AppHost.csproj
  Program.cs

ProcessingService.Domain/
  ProcessingService.Domain.csproj
  Entities/
    ImageMetadata.cs              # entity with EXIF fields + static Create()

ProcessingService.Application/
  ProcessingService.Application.csproj
  Common/
    Interfaces/
      IImageMetadataRepository.cs
      IBlobStorageService.cs
      IExifExtractorService.cs    # returns ExifData record
    Behaviours/
      ValidationBehaviour.cs      # IPipelineBehavior wiring FluentValidation into MediatR
  Images/
    Commands/
      ProcessImage/
        ProcessImageCommand.cs    # record: BlobName, ContainerName
        ProcessImageCommandHandler.cs
        ProcessImageCommandValidator.cs
  ExifData.cs                     # record: Camera, Lens, Iso, FocalLengthMm, Aperture, ExposureTime, TakenAt
  DependencyInjection.cs          # AddApplicationServices() — registers MediatR + FV pipeline

ProcessingService.Infrastructure/
  ProcessingService.Infrastructure.csproj
  Persistence/
    MongoDbContext.cs             # wraps IMongoDatabase, exposes IMongoCollection<ImageMetadata>
    ImageMetadataRepository.cs
  Storage/
    BlobStorageService.cs
  ExifExtraction/
    ExifExtractorService.cs       # MetadataExtractor → ExifData
  Options/
    ServiceBusOptions.cs
    BlobStorageOptions.cs
    MongoDbOptions.cs
  DependencyInjection.cs          # AddInfrastructureServices() — registers all impls + options

ProcessingService.Worker/
  ProcessingService.Worker.csproj
  Program.cs                      # WebApplication builder, MapHealthChecks("/health"), AddHostedService
  Workers/
    ImageProcessingWorker.cs      # BackgroundService: receives SB messages → IMediator.Send
  Models/
    ImageUploadedEvent.cs         # inline for now (move to Shared.Contracts later)
  appsettings.json
  appsettings.Development.json    # local emulator connection strings
```

---

## Key Implementation Details

### Domain — `ImageMetadata`
- Private setters; static `Create(string blobName, string containerName, ExifData exif)` factory
- All EXIF fields nullable (images may not have full metadata)

### Application — `ProcessImageCommand` handler flow
1. Validate command (FluentValidation via pipeline: `BlobName` and `ContainerName` not empty)
2. `IBlobStorageService.DownloadAsync(blobName, containerName)` → `Stream`
3. `IExifExtractorService.ExtractAsync(stream)` → `ExifData`
4. `ImageMetadata.Create(...)`
5. `IImageMetadataRepository.AddAsync(imageMetadata)`

### Application — `ValidationBehaviour<TRequest, TResponse>`
- Standard MediatR pipeline behaviour — runs all `IValidator<TRequest>` before the handler
- Throws `ValidationException` on failure

### Worker — `ImageProcessingWorker`
- `BackgroundService.ExecuteAsync`: registers `ServiceBusProcessor` message + error handlers, starts processing, awaits cancellation, stops processor on shutdown
- On each message: deserialize to `ImageUploadedEvent`, `await _mediator.Send(new ProcessImageCommand(...))`, `CompleteMessageAsync` on success; abandon on exception

### Infrastructure — `ExifExtractorService`
- Uses `MetadataExtractor.ImageMetadataReader.ReadMetadata(stream)`
- Maps `ExifIfd0Directory` and `ExifSubIfdDirectory` tags to `ExifData` fields

### AppHost — `Program.cs`
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongodb").WithDataVolume();

builder.AddProject<Projects.ProcessingService_Worker>("processing-service")
    .WithReference(mongo);

builder.Build().Run();
```

### Worker — `Program.cs`
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddMongoDBClient("mongodb");          // Aspire integration
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddHostedService<ImageProcessingWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

---

## NuGet Packages (additions to `Directory.Packages.props`)

| Package | Layer |
|---|---|
| `MediatR` | Application |
| `FluentValidation.DependencyInjectionExtensions` | Application |
| `Azure.Messaging.ServiceBus` | Infrastructure |
| `MetadataExtractor` | Infrastructure |
| `MongoDB.Driver` | Infrastructure |
| `Aspire.Hosting.AppHost` | AppHost |
| `Aspire.Hosting.MongoDB` | AppHost |
| `Aspire.MongoDB.Driver` | Worker (Aspire-integrated MongoDB client) |

`Azure.Storage.Blobs` is already in `Directory.Packages.props`.

---

## Solution File Changes (`open-lens.slnx`)

Add to existing `/services/` folder:
```xml
<Folder Name="/services/AppHost/">
  <Project Path="AppHost/AppHost.csproj" />
</Folder>
<Folder Name="/services/ProcessingService/" />
<Folder Name="/services/ProcessingService/src/Domain/">
  <Project Path="ProcessingService.Domain/ProcessingService.Domain.csproj" />
</Folder>
<Folder Name="/services/ProcessingService/src/Application/">
  <Project Path="ProcessingService.Application/ProcessingService.Application.csproj" />
</Folder>
<Folder Name="/services/ProcessingService/src/Infrastructure/">
  <Project Path="ProcessingService.Infrastructure/ProcessingService.Infrastructure.csproj" />
</Folder>
<Folder Name="/services/ProcessingService/src/Worker/">
  <Project Path="ProcessingService.Worker/ProcessingService.Worker.csproj" />
</Folder>
```

---

## Verification

1. `dotnet build open-lens.slnx` — all 5 new projects build cleanly
2. Run via Aspire: `dotnet run --project AppHost` → dashboard shows `processing-service` + `mongodb` resources
3. Publish a test `ImageUploadedEvent` message to the local Service Bus emulator → verify `ImageMetadata` document created in MongoDB
4. `GET http://localhost:<port>/health` → returns `200 Healthy`
