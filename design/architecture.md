# Solution Architecture

## Overview

DevContextMcp separates package index production from MCP retrieval:

```mermaid
flowchart TD
    Client[MCP client]
    Host[DevContextMcp.Server]
    Application[DevContextMcp.Server.Core]
    Indexer[DevContextMcp.Indexer.Core]
    Infrastructure[DevContextMcp.Infrastructure]
    IndexerCli[DevContextMcp.Indexer]
    NuGet[NuGet source]
    Database[(SQLite and FTS5)]

    Client -->|stdio or Streamable HTTP| Host
    Host --> Application
    Host --> Infrastructure
    Infrastructure --> Application
    Infrastructure --> Indexer
    IndexerCli --> Indexer
    IndexerCli --> Infrastructure
    Infrastructure --> NuGet
    Infrastructure --> Database
```

The Host is retrieval-only. The Indexer library defines the indexing use
cases, and the Indexer CLI is the separate one-shot composition root and sole
index writer process.

## Projects

### DevContextMcp.Server.Core

Contains MCP wire contracts, retrieval models and abstractions, and retrieval
services. It has no project references and does not depend on SQLite, NuGet, or
MCP transport implementations.

### DevContextMcp.Indexer.Core

Contains the inner indexing feature boundary:

- Source-neutral indexing models.
- Ports for source access, package processing, configuration, and persistence.
- `IIndexCoordinator` and indexing orchestration.

Indexer Core has no project references and contains no hosting, NuGet client,
archive-processing, or SQLite implementation packages.

### DevContextMcp.Infrastructure

Implements Application retrieval abstractions and Indexer ports:

- Read-only SQLite and FTS5 retrieval.
- NuGet discovery, metadata lookup, and bounded package download.
- Safe archive inspection and metadata-only symbol extraction.
- Document chunking and SHA-256 hashing.
- SQLite schema migration, atomic publication, FTS5 writes, and run history.
- Local dependency diagnostics.

Infrastructure references Application and Indexer Core. Retrieval and indexing use
separate registration methods so the Host never composes index writers.

### DevContextMcp.Server

Is the MCP executable and retrieval composition root:

- Loads and validates Host configuration.
- Owns Host transport and retrieval option contracts and validation.
- Registers Application and retrieval Infrastructure services.
- Selects stdio or stateless Streamable HTTP.
- Exposes four MCP tools and NuGet resource templates.
- Opens the SQLite index read-only.

The Host does not contact NuGet sources or register Indexer services.

### DevContextMcp.Indexer

Is the one-shot indexing executable and composition root:

- Owns indexing configuration, validation, and `appsettings.json`.
- Loads and caches externally provisioned package-policy JSON files at startup.
- Converts CLI options into source-neutral Indexer settings.
- Registers Indexer orchestration and Infrastructure indexing adapters.
- Runs every configured source once and reports summaries.

It exits `0` for success or no configured sources. It exits `1` for partial
success, failure, invalid configuration, or cancellation. Recurring execution
uses an external scheduler, and only one CLI process may write to a given
SQLite database at a time.

## Dependency Rules

```text
Application    -> no project references
Configuration  -> no project references
Indexer.Core   -> no project references
Infrastructure -> Application + Indexer.Core
Host           -> Application + Configuration + Infrastructure
Indexer        -> Indexer.Core + Infrastructure
Tests          -> projects required by each scenario
```

Architecture tests enforce this graph and verify that the former indexing
projects are absent.

## Indexing Flow

```mermaid
sequenceDiagram
    participant CLI as Indexer CLI
    participant Coordinator as IndexCoordinator
    participant Source as Infrastructure IPackageSourceClient
    participant Processor as Infrastructure IPackageProcessor
    participant Store as Infrastructure IIndexStore
    participant DB as SQLite/FTS5

    CLI->>Coordinator: IndexAllAsync
    Coordinator->>Store: Initialize or migrate database
    Coordinator->>Source: Discover package versions
    loop Each package version
        Coordinator->>Source: Download bounded package
        Coordinator->>Processor: Extract index data
    end
    Coordinator->>Store: Publish source atomically
    Store->>DB: Replace changed rows and update FTS
```

Package content hashes and deterministic IDs make repeated runs idempotent.
Unchanged package content is not rewritten. `index_runs` intentionally records
each execution, while canonical package, version, artifact, and symbol rows are
not duplicated.

Feed definitions come from normal .NET configuration. Each feed name is also
its environment slug. Exact package-selection policies come from an external
folder and are joined to the feed with the matching environment. Each feed is
discovered and published once with the complete active package set. Package
deletion tombstones are applied in the same transaction without contacting the
feed. Feeds with no matching package files are omitted so existing rows are
not pruned.

## Retrieval Flow

```mermaid
sequenceDiagram
    participant Client as MCP client
    participant Tool as Host MCP tool
    participant Handler as Retrieval handler
    participant Store as INuGetReadStore
    participant DB as SQLite/FTS5

    Client->>Tool: MCP request
    Tool->>Handler: Typed request contract
    Handler->>Store: Version-scoped query
    Store->>DB: Read-only SQL or FTS5 search
    DB-->>Store: Indexed records
    Store-->>Handler: Retrieval models
    Handler-->>Tool: Response with citations
    Tool-->>Client: Structured MCP result
```

Qualified `nuget:{environment}/{packageId}` identifiers never cross
environments. Legacy identifiers select by `EnvironmentOrder` and
`SourceOrder`. All evidence remains isolated to one selected package version.

## Composition

- `Application.AddApplication()` registers retrieval handlers and policies.
- `Indexer.Core.AddIndexer()` registers indexing orchestration only.
- `Infrastructure.AddRetrievalInfrastructure()` registers read-only retrieval
  adapters.
- `Infrastructure.AddIndexingInfrastructure()` registers concrete indexing
  adapters.
- `Host.AddDevContextMcpCore()` binds Host configuration and composes retrieval.
- `Indexer.AddIndexerCli(configuration)` binds CLI configuration and
  composes the complete indexing pipeline.
- `Host.WithDevContextMcpTools()` publishes tools and resources.

## Testing Strategy

- Unit tests cover configuration, archive safety, chunking, symbol extraction,
  version selection, serialization, registration boundaries, and architecture
  rules.
- Integration tests index local fixture packages into temporary SQLite
  databases and exercise retrieval end to end.
- Child-process tests verify Indexer CLI exit codes and Host stdio/HTTP
  behavior.
- Idempotency tests run indexing repeatedly and verify canonical rows remain
  unique while run history grows.

## Extension Guidelines

- Keep MCP wire shapes in `Application.Contracts`.
- Add retrieval integrations behind Application abstractions.
- Keep indexing models, ports, and orchestration in Indexer Core.
- Implement external indexing concerns in Infrastructure.
- Keep executable configuration and lifecycle behavior in Indexer CLI.
- Never execute package assemblies.
- Preserve package-version isolation and citation traceability.
