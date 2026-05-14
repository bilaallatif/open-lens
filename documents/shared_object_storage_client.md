# Design: Shared Object Storage Client

## Context

Both `UploadService` and `ProcessingService` interact with Azure Blob Storage. Before this change each service had its own blob storage options class, its own SDK client registration, and (in ProcessingService) its own service-specific interface (`IImageStorageClient`). The duplication created two sources of truth for the same infrastructure concern.

---

## Decision

Centralise blob storage into two shared projects:

```
Shared.ObjectStorage.Application/    — interface only, no Azure SDK dependency
  IObjectStorageClient.cs

Shared.ObjectStorage.Infrastructure/  — Azure SDK implementation + DI registration
  ObjectStorageClient.cs
  ObjectStorageOptions.cs
  DependencyInjection.cs
```

A single `IObjectStorageClient` interface is used directly by both services. Service-specific wrapper interfaces (e.g. `IImageStorageClient`) were considered and rejected — they added a layer of indirection without meaningful domain value, since neither service adds behaviour beyond the raw blob operations.

---

## Key Design Choices

### Single interface, not service-specific interfaces

The earlier design in `plans/shared.md` proposed keeping service-specific interfaces (`IBlobDownloadService`, `ISasUrlService`) in each service's Application layer, with the shared project only registering the SDK client.

This was revised because the service-specific interfaces were thin renames of the same operations with no added domain logic. The overhead of maintaining separate interfaces, separate implementations, and separate registrations was not justified. If a service ever needs behaviour beyond raw blob access (e.g. validation, transformation), a service-specific interface can be introduced at that point.

### Application / Infrastructure split

`IObjectStorageClient` lives in `Shared.ObjectStorage.Application` (no Azure SDK reference). The implementation lives in `Shared.ObjectStorage.Infrastructure`. This preserves the clean architecture dependency rule: service Application layers reference the interface project only; service Infrastructure layers reference the implementation project.

### `BlobContainerClient`, not `BlobServiceClient`

The underlying SDK client is `BlobContainerClient` (container-scoped) rather than `BlobServiceClient` (account-scoped). The container is fixed at startup via `ObjectStorageOptions.ContainerName`. This simplifies the implementation — no per-request container resolution — and makes the registration explicit about which container the service operates on.

### Options validation at startup

`ObjectStorageOptions` uses `[Required]` data annotations combined with `.ValidateDataAnnotations().ValidateOnStart()`. The C# `required` modifier was not used because `IConfiguration.Bind` operates via reflection and bypasses it at runtime; `[Required]` with `ValidateOnStart` enforces validation before the application accepts traffic.

### Stream disposal

`GetObjectStreamAsync` returns a network-backed stream owned by the caller. The command handler disposes it with `await using` to ensure the underlying HTTP connection is released on all exit paths, including exceptions thrown by downstream processing.

---

## Known Limitations

### `GetObjectUploadUri` requires a shared key credential

`BlobClient.GenerateSasUri` requires the `BlobContainerClient` to have been constructed with a `StorageSharedKeyCredential`. The current registration uses a connection string (which embeds the account key) to unblock development.

The intended production authentication is RBAC via a service principal. This requires switching to a user delegation SAS: calling `BlobServiceClient.GetUserDelegationKeyAsync` and signing the `BlobSasBuilder` with the resulting key. This will require:

- `BlobServiceClient` to be registered alongside `BlobContainerClient`
- The service principal to hold the `Storage Blob Delegator` role on the storage account
- `GetObjectUploadUri` to become `async Task<Uri>`

### Testing `GetObjectUploadUri` against Azurite

User delegation SAS can be tested against Azurite by starting it with the `--oauth basic` flag. The Testcontainers Azurite builder supports this via `.WithCommand("--oauth", "basic")`. A credential compatible with Azurite's OAuth mode (e.g. `AzureCliCredential`) must be used in the test host. This is deferred until the RBAC migration above is completed.
