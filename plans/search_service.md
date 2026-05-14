# Plan: SearchService

## Context

`SearchService` is an ASP.NET Core Web API that exposes a single endpoint for filtered, paginated search over the `image_metadata` MongoDB collection that `ProcessingService` writes to.

Architecture mirrors the other services: Clean Architecture (Domain / Application / Infrastructure / API), CQRS via MediatR. Read-only — no writes, no Service Bus, no Blob Storage dependency.

Alongside the new service, the Mongo plumbing currently embedded in `ProcessingService` is lifted into two shared projects. The persistence **schema** (`ImageMetadataDocument`, `ExifDataDocument`) is shared too — `ProcessingService` writes and `SearchService` reads from the same collection, so keeping two copies would invite silent field drift. Each service still owns its own **domain entity** (`ImageMetadata`, `ExifData`); only the BSON-mapped document is shared.

> Note: this aligns with the MongoDB section of `plans/shared.md`. The naming `Shared.Mongo.Infrastructure` is slightly imprecise once concrete schema types live there — if more shared schemas appear, lift them into a dedicated `Shared.Schemas` project.

---

## Projects to Create

| Project | SDK | Solution folder |
|---|---|---|
| `Shared.Mongo.Application` | `Microsoft.NET.Sdk` | `/services/Shared/Mongo/` |
| `Shared.Mongo.Infrastructure` | `Microsoft.NET.Sdk` | `/services/Shared/Mongo/` |
| `SearchService.Domain` | `Microsoft.NET.Sdk` | `/services/SearchService/src/` |
| `SearchService.Application` | `Microsoft.NET.Sdk` | `/services/SearchService/src/` |
| `SearchService.Infrastructure` | `Microsoft.NET.Sdk` | `/services/SearchService/src/` |
| `SearchService.API` | `Microsoft.NET.Sdk.Web` | `/services/SearchService/src/` |
| `SearchService.API.IntegrationTests` | `Microsoft.NET.Sdk` | `/services/SearchService/tests/` |

---

## Physical Directory Structure

```
Shared.Mongo.Application/
  Shared.Mongo.Application.csproj
  BaseEntity.cs                       # moved from ProcessingService.Domain.Common
  IRepository.cs                      # moved from ProcessingService.Application.Common.Interfaces

Shared.Mongo.Infrastructure/
  Shared.Mongo.Infrastructure.csproj
  BaseDocument.cs                     # moved from ProcessingService.Infrastructure.Data.Documents
  MongoRepository.cs                  # moved from ProcessingService.Infrastructure.Data
  MongoDbOptions.cs                   # moved from ProcessingService.Infrastructure.Options
  DependencyInjection.cs              # AddMongo() — registers options + IMongoClient + IMongoDatabase
  Documents/
    ImageMetadataDocument.cs          # moved from ProcessingService.Infrastructure.Data.Documents
    ExifDataDocument.cs               # moved from ProcessingService.Infrastructure.Data.Documents

SearchService.Domain/
  SearchService.Domain.csproj
  Entities/
    ImageMetadata.cs                  # : BaseEntity — read-side projection (own copy of the domain entity)
    ExifData.cs                       # own copy of the EXIF shape (domain, not BSON)

SearchService.Application/
  SearchService.Application.csproj
  Common/
    PagedResult.cs                    # record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
    Interfaces/
      IImageMetadataSearchRepository.cs
  Images/
    Queries/
      SearchImages/
        SearchImagesQuery.cs          # IRequest<PagedResult<ImageMetadataDto>>
        SearchImagesQueryHandler.cs
        ImageMetadataDto.cs
        ExifDataDto.cs
        ImageSearchCriteria.cs        # value type passed into the repository
  DependencyInjection.cs              # AddApplicationServices() — registers MediatR

SearchService.Infrastructure/
  SearchService.Infrastructure.csproj
  Data/
    ImageMetadataSearchRepository.cs  # IImageMetadataSearchRepository impl — uses Shared.Mongo.Infrastructure.Documents.ImageMetadataDocument
  DependencyInjection.cs              # AddInfrastructureServices() — calls AddMongo(), registers repo

SearchService.API/
  SearchService.API.csproj
  Program.cs
  Endpoints/
    SearchEndpoints.cs                # MapSearchEndpoints() — GET /images
  appsettings.json
  appsettings.Development.json

SearchService.API.IntegrationTests/
  SearchService.API.IntegrationTests.csproj
  SearchImagesTests.cs                # Testcontainers MongoDb fixture, seeds documents, exercises filter + paging
  HealthTests.cs
```

---

## Endpoint Specification

### `GET /images`

| Query param | Type | Behaviour |
|---|---|---|
| `cameraMake` | `string?` | Exact match on `ExifData.CameraMake` |
| `cameraModel` | `string?` | Exact match on `ExifData.CameraModel` |
| `lensMake` | `string?` | Exact match on `ExifData.LensMake` |
| `lensModel` | `string?` | Exact match on `ExifData.LensModel` |
| `isoMin` | `int?` | Inclusive lower bound on `ExifData.Iso` |
| `isoMax` | `int?` | Inclusive upper bound on `ExifData.Iso` |
| `takenAfter` | `DateTimeOffset?` | Inclusive lower bound on `ExifData.TakenAt` |
| `takenBefore` | `DateTimeOffset?` | Inclusive upper bound on `ExifData.TakenAt` |
| `page` | `int` (default `1`) | 1-based page number |
| `pageSize` | `int` (default `20`, capped at `100`) | Items per page |

**Sort:** `ExifData.TakenAt` desc, tiebreak `CreatedAt` desc. Documents with null `TakenAt` sort to the end.

**Response:** `200 OK`

```json
{
  "items": [
    {
      "id": "guid",
      "blobName": "string",
      "createdAt": "iso-8601",
      "exifData": {
        "cameraMake": "string?",
        "cameraModel": "string?",
        "lensMake": "string?",
        "lensModel": "string?",
        "iso": 0,
        "focalLength": "string?",
        "fNumber": "string?",
        "takenAt": "iso-8601?",
        "latitude": 0.0,
        "longitude": 0.0
      }
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 123
}
```

**Validation:**
- `page >= 1`, `pageSize >= 1`. Out-of-range values are clamped (`page` to `1`, `pageSize` to `[1, 100]`) rather than rejected, to keep the endpoint forgiving.
- `isoMin <= isoMax` and `takenAfter <= takenBefore` — invalid combinations produce `400 Bad Request`.

---

## Key Implementation Details

### `IImageMetadataSearchRepository`

```csharp
public interface IImageMetadataSearchRepository
{
    Task<PagedResult<ImageMetadata>> SearchAsync(
        ImageSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
```

The generic `IRepository<T>` from `Shared.Mongo.Application` is intentionally not reused — filtered, paginated search does not fit the CRUD shape.

### `ImageMetadataSearchRepository` — filter composition

Uses `FilterDefinitionBuilder<ImageMetadataDocument>`:

- Each non-null criterion contributes one filter; all combine with `Builders<T>.Filter.And(...)`.
- If no criteria are supplied, the filter is `Builders<T>.Filter.Empty`.
- Pagination: `CountDocumentsAsync(filter)` for the total, then `Find(filter).Sort(...).Skip((page-1) * pageSize).Limit(pageSize).ToListAsync()`.
- The count and find run sequentially (single connection, simpler error handling). If latency becomes an issue, parallelise with `Task.WhenAll`.

### `SearchImagesQuery` handler

Thin: clamps paging, validates ranges, builds `ImageSearchCriteria`, delegates to the repository, maps `ImageMetadata` → `ImageMetadataDto`.

### `SearchService.API/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health");
app.MapSearchEndpoints();

app.Run();
```

### Config

`appsettings.json`:
```json
{
  "MongoDB": { "DatabaseName": "open-lens" }
}
```

`appsettings.Development.json`:
```json
{
  "MongoDB": { "ConnectionString": "mongodb://localhost:27017" }
}
```

Database name matches what `ProcessingService` writes to.

---

## Refactor: ProcessingService → use Shared.Mongo

Strictly in scope for this plan — without it the new shared projects have only one consumer.

| Remove from ProcessingService | Replace with |
|---|---|
| `ProcessingService.Domain/Common/BaseEntity.cs` | Use `Shared.Mongo.Application.BaseEntity` |
| `ProcessingService.Application/Common/Interfaces/IRepository.cs` | Use `Shared.Mongo.Application.IRepository<T>` |
| `ProcessingService.Infrastructure/Data/Documents/BaseDocument.cs` | Use `Shared.Mongo.Infrastructure.BaseDocument` |
| `ProcessingService.Infrastructure/Data/Documents/ImageMetadataDocument.cs` | Use `Shared.Mongo.Infrastructure.Documents.ImageMetadataDocument` |
| `ProcessingService.Infrastructure/Data/Documents/ExifDataDocument.cs` | Use `Shared.Mongo.Infrastructure.Documents.ExifDataDocument` |
| `ProcessingService.Infrastructure/Data/MongoRepository.cs` | Use `Shared.Mongo.Infrastructure.MongoRepository<,>` |
| `ProcessingService.Infrastructure/Options/MongoDbOptions.cs` | Use `Shared.Mongo.Infrastructure.MongoDbOptions` |
| Inline Mongo registration in `ProcessingService.Infrastructure/DependencyInjection.cs` (options + `IMongoClient` + `IMongoDatabase`) | `services.AddMongo();` |

`ProcessingService.Application` gains a reference to `Shared.Mongo.Application`; `ProcessingService.Infrastructure` gains a reference to `Shared.Mongo.Infrastructure`. Namespace updates ripple through `using` directives.

---

## Tests

`SearchService.API.IntegrationTests` follows the same pattern as the existing integration test suites.

- **`MongoFixture`** (Testcontainers `MongoDbBuilder`) shared across the test class via `IAsyncLifetime` / `IClassFixture`.
- **`WebApplicationFactory<Program>`** with the Mongo connection string overridden to the container's port.
- **Seed:** ~10 `ImageMetadataDocument`s spanning the filter axes (two camera makes, three lens models, ISOs from 100–6400, dates across a 12-month range, plus one with null `TakenAt`).
- **Scenarios:**
  - No filters → returns all, paginated, default sort
  - Single filter per axis (camera, lens, ISO range, date range)
  - Combined filters (camera + ISO range)
  - Pagination: page 2 of pageSize 3, verify `items.Count`, `totalCount`, ordering stable across pages
  - `pageSize=500` clamped to 100
  - `isoMin > isoMax` → `400`
  - Null `TakenAt` doc sorts to the end
- **`HealthTests`** mirrors the UploadService one — `GET /health` returns `200`.

---

## NuGet Packages

No new packages — `MongoDB.Driver`, `MediatR`, `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore.SwaggerUI`, `Testcontainers.MongoDb`, `Microsoft.AspNetCore.Mvc.Testing` are all already in `Directory.Packages.props`.

---

## Solution File Changes (`open-lens.slnx`)

```xml
<Folder Name="/services/Shared/Mongo/">
  <Project Path="Shared.Mongo.Application/Shared.Mongo.Application.csproj" />
  <Project Path="Shared.Mongo.Infrastructure/Shared.Mongo.Infrastructure.csproj" />
</Folder>

<Folder Name="/services/SearchService/" />
<Folder Name="/services/SearchService/src/">
  <Project Path="SearchService.API/SearchService.API.csproj" />
  <Project Path="SearchService.Application/SearchService.Application.csproj" />
  <Project Path="SearchService.Domain/SearchService.Domain.csproj" />
  <Project Path="SearchService.Infrastructure/SearchService.Infrastructure.csproj" />
</Folder>
<Folder Name="/services/SearchService/tests/">
  <Project Path="SearchService.API.IntegrationTests/SearchService.API.IntegrationTests.csproj" />
</Folder>
```

---

## Verification

1. `dotnet build open-lens.slnx` — all new projects + the refactored ProcessingService build cleanly.
2. `dotnet test` — existing ProcessingService and UploadService integration tests still pass after the Shared.Mongo refactor; new SearchService tests pass.
3. Run end-to-end locally: start docker-compose (Azurite + Service Bus emulator) + a Mongo container, run UploadService + ProcessingService + SearchService. Upload an image, wait for processing, then `GET http://localhost:<port>/images?cameraMake=...` returns the document.
4. `GET /health` returns `200 Healthy`.
5. Swagger UI at `/swagger` lists `GET /images` with all query parameters and the response schema.
