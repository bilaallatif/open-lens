# Image Processing Application — Spec

## Overview

A microservice-based image processing application. Users upload images via presigned URLs, which are asynchronously processed to extract EXIF metadata and stored in MongoDB for search. An MCP server exposes the image library to AI clients.

## Tech Stack

- **Runtime:** .NET / C#
- **Architecture:** Microservices
- **Observability:** OpenTelemetry
- **Database:** CosmosDB (MongoDB API)
- **Messaging:** Azure Service Bus
- **Storage:** Azure Blob Storage
- **Hosting:** Azure Container Apps (ACA) + Azure Container Registry (ACR)
- **Frontend:** Angular
- **AI integration:** MCP server

---

## Services

### `upload-service`
**ASP.NET Core Web API**
- `GET /presigned-url` — generates a presigned Azure Blob Storage URL for direct client upload
- `POST /confirm` — local only, publishes directly to Service Bus queue when `ENVIRONMENT=local`
- No image processing or event publishing logic in Azure — Event Grid handles the trigger

### `processing-service`
**ASP.NET Core Worker Service**
- No REST endpoints — long-running background worker consuming from the `image-uploaded` Service Bus queue
- Downloads the image from Blob Storage
- Extracts EXIF metadata using `MetadataExtractor`
- Persists structured metadata directly to MongoDB
- Exposes `GET /health` for ACA health probes only

### `search-service`
**ASP.NET Core Web API**
- `GET /images` — search and list images, filterable by camera, lens, ISO, date, etc.

### `mcp-server`
**ASP.NET Core (MCP SDK over SSE)**
- Not a REST API — communicates over the MCP protocol via SSE transport
- Exposes MCP tools rather than REST endpoints
- Example tools: `search_images(lens: "135mm")`, `get_image_stats()`
- Allows MCP-compatible clients (Claude, Cursor) to query the image library

---

## Communication

```
Upload flow:
  Angular ──[GET presigned URL]──► upload-service
  Angular ──[PUT image]──────────► Azure Blob Storage (direct)
                                          │
                                 [blob created event]
                                          ▼
                                     Event Grid
                                          │
                              [forwards to Service Bus queue]
                                          ▼
                               processing-service ──[write]──► CosmosDB

Search flow:
  Angular ──[search / list]──► search-service ──[read]──► CosmosDB

MCP clients (Claude, Cursor):
  └─► mcp-server ──► search-service
```

---

## Authentication

### Identity Provider

Azure AD (Entra ID) is the identity provider. Two app registrations are required:

| Registration | Purpose |
|---|---|
| SPA (Angular) | Defines the OAuth client — client ID, allowed redirect URIs, API permissions |
| API | Exposes the `access_as_user` scope that the SPA requests on behalf of the logged-in user |

### User Login Flow (Delegated)

Used by Angular to authenticate users and call backend services.

```
User ──logs in──► Azure AD login page
                      │
            issues delegated access token:
            subject = logged-in user
            scope = access_as_user
                      │
                     ▼
          Angular (MSAL stores token)
                      │
         attaches token to API requests
                      │
               ▼              ▼
        upload-service    search-service
        (ACA EasyAuth validates token)
```

Angular uses `@azure/msal-angular` with Authorization Code + PKCE flow. `MsalInterceptor` automatically attaches the access token to outgoing HTTP requests. Token refresh is handled silently by MSAL.

### Service-to-Azure Auth (Managed Identity)

Each ACA container app is assigned a managed identity with the minimum RBAC roles required to access the Azure resources it needs. No connection strings or secrets are used.

| Service | Resource | Role |
|---|---|---|
| `upload-service` | Blob Storage | Storage Blob Delegator (to generate user delegation SAS URLs) |
| `processing-service` | Blob Storage | Storage Blob Data Reader |
| `processing-service` | Service Bus | Azure Service Bus Data Receiver |
| `processing-service` | CosmosDB | Cosmos DB Built-in Data Contributor |
| `search-service` | CosmosDB | Cosmos DB Built-in Data Reader |

`processing-service` does not expose an HTTP endpoint — it is triggered by the Service Bus queue, so no inbound auth is required.

### CI/CD Auth

GitHub Actions authenticates to Azure using a **dedicated app registration** with a federated identity credential that trusts GitHub's OIDC issuer (`https://token.actions.githubusercontent.com`). GitHub Actions exchanges its OIDC token for an Azure AD access token scoped to that service principal — no client secrets stored in GitHub.

The service principal requires the following RBAC roles:
- `AcrPush` on the ACR — to push container images
- `Contributor` on the resource group — to run Bicep deployments

### What Requires Auth

| Service | Auth required? | Method |
|---|---|---|
| `upload-service` | Yes | ACA EasyAuth (Azure AD) |
| `search-service` | Yes | ACA EasyAuth (Azure AD) |
| `processing-service` | No | Triggered by Service Bus |
| `mcp-server` | TBD | Depends on clients |

---

## OpenTelemetry

Each service exports traces and metrics via the OTLP exporter. The collector differs by environment:

| Environment | Collector |
|---|---|
| Azure | App Insights (already provisioned in `monitoring.bicep`) |
| Local | Jaeger (Docker Compose service) |

Key spans:

- `Angular → search-service` HTTP call latency
- `processing-service`: EXIF extraction duration, image file size
- Service Bus message lag (publish timestamp → consume timestamp)

---

## Deployment

All infrastructure is defined as code using Bicep and deployed via GitHub Actions.

### Bicep Module Structure

```
infra/
  main.bicep                  # Entry point — wires up all modules
  modules/
    container-registry.bicep  # ACR
    container-apps-env.bicep  # Shared ACA environment
    container-apps.bicep      # One container app per service
    service-bus.bicep         # Namespace + image-uploaded queue
    event-grid.bicep          # Event Grid subscription on Blob Storage (blob created → Service Bus queue)
    key-vault.bicep           # Key Vault for any non-Azure secrets
    storage.bicep             # Blob Storage account + container
    cosmos-db.bicep           # CosmosDB (MongoDB API), serverless
    monitoring.bicep          # Log Analytics workspace + App Insights
```

### Provisioned Resources

| Resource | Detail |
|---|---|
| ACR | Stores container images |
| ACA Environment | Shared environment for all container apps |
| Container Apps | One per service: `upload-service`, `processing-service`, `search-service`, `mcp-server` |
| Service Bus | Namespace with one queue: `image-uploaded` |
| Event Grid | Subscription on Blob Storage — forwards blob created events to the Service Bus queue |
| Key Vault | Stores any secrets not covered by managed identity |
| Blob Storage | Account with a single container for uploaded images |
| CosmosDB | MongoDB API, serverless, one database, one collection for image metadata |
| Log Analytics | Backing store for ACA logs and OTel traces |
| App Insights | OTel collector endpoint for all services |

### CI/CD Pipeline (GitHub Actions)

Two workflows:

**`build.yml`** — triggers on push to `main`:
1. Build each service's Docker image
2. Push images to ACR with the commit SHA as the tag

**`deploy.yml`** — triggers after `build.yml` succeeds:
1. Run `az deployment group create` with `infra/main.bicep`
2. Pass the new image tag as a Bicep parameter — ACA pulls the updated image on next revision

### Deployment Order

Bicep handles dependency ordering automatically, but the logical sequence is:

1. ACR, Service Bus, Blob Storage, CosmosDB, Log Analytics, Key Vault (infrastructure)
2. ACA Environment (depends on Log Analytics)
3. Container Apps (depends on ACR + ACA Environment + all backing services)

---

## Local Development

All services can be run locally via Docker Compose, with Azure dependencies replaced by local emulators.

### Emulators

| Azure Service | Local Equivalent |
|---|---|
| Azure Blob Storage | Azurite (official Microsoft emulator) |
| Azure Service Bus | Azure Service Bus Emulator (official) |
| CosmosDB (MongoDB API) | MongoDB container (driver-compatible, no emulator needed) |
| Event Grid | Not emulated — replaced by a `POST /confirm` endpoint on `upload-service` (see below) |

### Docker Compose

```
docker-compose.yml
  ├── azurite             # Blob Storage emulator
  ├── servicebus          # Service Bus emulator
  ├── mongodb             # Local MongoDB
  ├── upload-service
  ├── processing-service
  ├── search-service
  ├── mcp-server
  ├── jaeger              # OTel traces UI
  └── frontend            # Angular via nginx
```

Each .NET service uses `appsettings.Development.json` to point at local emulator connection strings instead of real Azure endpoints. Switching between local and Azure is config-only.

### Event Grid Locally

The Event Grid emulator does not support system topics, and Azurite does not fire blob created events. Locally, `upload-service` exposes a `POST /confirm` endpoint that the Angular app calls after a successful upload. When `ENVIRONMENT=local`, `upload-service` publishes directly to the Service Bus queue instead of relying on Event Grid.

```
Local upload flow:
  Angular ──[GET presigned URL]──► upload-service
  Angular ──[PUT image]──────────► Azurite
  Angular ──[POST /confirm]──────► upload-service ──[publish]──► Service Bus queue
                                                                        │
                                                                        ▼
                                                             processing-service
```

In Azure, the `POST /confirm` endpoint is not exposed — Event Grid handles the trigger automatically.

### Authentication Locally

Azure AD has no local emulator. The recommended approach is to point MSAL at your real Azure AD tenant during local development — the login flow is identical to production. As a fallback, auth middleware can be disabled via an environment variable (`AUTH_ENABLED=false`) to speed up local testing without a browser login.

---

## Project Structure

```
proto/
├── proto.sln
│
├── src/
│   ├── services/
│   │   ├── UploadService/          # ASP.NET Core Web API
│   │   │   ├── Controllers/
│   │   │   ├── appsettings.json
│   │   │   └── appsettings.Development.json
│   │   │
│   │   ├── ProcessingService/      # ASP.NET Core Worker Service
│   │   │   ├── Workers/
│   │   │   ├── appsettings.json
│   │   │   └── appsettings.Development.json
│   │   │
│   │   ├── SearchService/          # ASP.NET Core Web API
│   │   │   ├── Controllers/
│   │   │   ├── appsettings.json
│   │   │   └── appsettings.Development.json
│   │   │
│   │   └── McpServer/              # ASP.NET Core (MCP SDK over SSE)
│   │       ├── Tools/
│   │       ├── appsettings.json
│   │       └── appsettings.Development.json
│   │
│   └── shared/
│       └── Shared.Contracts/       # Class library — shared event/message types
│           └── Events/
│               └── ImageUploadedEvent.cs
│
├── frontend/                       # Angular app (ng new)
│
├── infra/
│   ├── main.bicep
│   └── modules/
│       ├── container-registry.bicep
│       ├── container-apps-env.bicep
│       ├── container-apps.bicep
│       ├── service-bus.bicep
│       ├── event-grid.bicep
│       ├── storage.bicep
│       ├── cosmos-db.bicep
│       ├── key-vault.bicep
│       └── monitoring.bicep
│
├── .github/
│   └── workflows/
│       ├── build.yml
│       └── deploy.yml
│
└── docker-compose.yml
```

- `Shared.Contracts` is referenced by all services — defines the `ImageUploadedEvent` type so the Service Bus message shape is in one place
- Each service has a `Dockerfile` alongside its `.csproj`
- Each service has `appsettings.Development.json` pointing at local emulator endpoints
- `frontend/` is populated by running `ng new` inside the directory

---

## MVP Scope

1. Upload image via presigned URL
2. Async EXIF metadata extraction
3. Store metadata in MongoDB
4. Search images by metadata fields (e.g. lens, camera model, ISO)
5. MCP server with at least one search tool
6. Angular UI for upload and search
7. OTel traces visible across all services
