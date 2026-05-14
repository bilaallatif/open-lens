# Plan: Shared.Infrastructure

## Purpose

A single shared library consumed by all services in the monorepo. It provides shared **mechanism** — SDK client registration, base classes, options, and document types — but not behaviour. Each service's application layer defines its own interfaces that expose only the slice of capability it needs.

---

## What to Share vs. Not Share

| Thing | Share? | Reason |
|---|---|---|
| `MongoRepository<T,TDoc>` base class | Yes | Generic plumbing, no domain knowledge |
| `BaseDocument` | Yes | Generic plumbing |
| `ImageMetadataDocument` | Yes | Both SearchService and ProcessingService read from the same collection — the document type is a shared schema contract. Two copies invite drift bugs. |
| `MongoDbOptions` | Yes | Same config shape across services |
| `BlobStorageOptions` | Yes | Same config shape across services |
| `BlobContainerClient` DI registration | Yes | Same SDK wiring pattern |
| `IBlobDownloadService` / `ISasUrlService` | No | Each service exposes only its required slice via its own application interface |
| Domain entities (`ImageMetadata`, `ExifData`) | No | Each service owns its domain model; domain boundaries must stay explicit |
| `IRepository<T>` / `IImageSearchRepository` | No | Operation profiles differ per service — these belong in each service's Application layer |

---

## Structure

```
Shared.Infrastructure/
  Shared.Infrastructure.csproj
  MongoDB/
    BaseDocument.cs
    MongoRepository.cs
    Documents/
      ImageMetadataDocument.cs
      ExifDataDocument.cs
    Options/
      MongoDbOptions.cs
    Extensions/
      MongoDbServiceCollectionExtensions.cs     # AddMongoDb()
  BlobStorage/
    Options/
      BlobStorageOptions.cs
    Extensions/
      BlobStorageServiceCollectionExtensions.cs  # AddBlobStorageClient()
```

---

## Blob Storage — Interface Segregation Across Services

`Shared.Infrastructure` registers the `BlobContainerClient` and exposes `BlobStorageOptions`. Each service's application layer defines a narrowly scoped interface, implemented in that service's infrastructure layer.

```
Shared.Infrastructure
  └── BlobContainerClient (registered via AddBlobStorageClient())

UploadService.Application
  └── ISasUrlService
        GenerateSasUrlAsync(string blobName) → SasUrl

UploadService.Infrastructure
  └── SasUrlService : ISasUrlService
        (uses BlobContainerClient to generate user delegation SAS)

ProcessingService.Application
  └── IBlobDownloadService
        DownloadAsync(string blobName) → Stream

ProcessingService.Infrastructure
  └── BlobDownloadService : IBlobDownloadService
        (uses BlobContainerClient to download blob content)
```

The application layer of each service is unaware of Azure SDK types — it depends only on its own interface. The infrastructure layer takes the `BlobContainerClient` as a dependency and implements the interface.

---

## MongoDB — Shared Schema Contract

ProcessingService writes and SearchService reads from the same `image_metadata` collection. `ImageMetadataDocument` lives in `Shared.Infrastructure` because it represents the schema of that collection — keeping two copies would risk silent field drift.

Each service still owns its domain entity and repository interface:

```
Shared.Infrastructure
  └── ImageMetadataDocument (schema contract for image_metadata collection)

ProcessingService.Domain
  └── ImageMetadata (write-side aggregate)
ProcessingService.Application
  └── IRepository<ImageMetadata> (CRUD)
ProcessingService.Infrastructure
  └── ImageMetadataRepository : MongoRepository<ImageMetadata, ImageMetadataDocument>

SearchService.Domain
  └── ImageMetadata (read-side projection — may differ over time)
SearchService.Application
  └── IImageSearchRepository (SearchAsync with filters, GetByIdAsync — read-only)
SearchService.Infrastructure
  └── ImageSearchRepository : MongoRepository<ImageMetadata, ImageMetadataDocument>
```

---

## Rule of Thumb

> Share **mechanism** (SDK clients, base classes, options, DI registration, shared schema contracts).
> Each service owns its **behaviour** (what it does with those clients, its domain model, its application interfaces).
