# MCP Documentation Server Specification

## 1. Purpose

Build a .NET MCP server that helps coding agents discover and correctly use
approved NuGet packages. Generated API clients are distributed and indexed as
ordinary NuGet packages.

The server indexes package metadata, README files, XML documentation, managed
assembly symbols, and selected packaged text artifacts. Every answer must be
grounded in indexed evidence and traceable through a stable citation.

## 2. Goals

- Resolve a package from an exact ID or implementation concept.
- Select the correct package version deterministically.
- Retrieve package-authored documentation without mixing versions.
- Find public types and members with their real signatures.
- Support generated API-client packages through the same NuGet workflow.
- Expose cited package artifacts and symbols as read-only MCP resources.
- Return explicit machine-readable failures instead of inventing APIs.

## 3. Non-Goals

- Generate API clients.
- Execute package assemblies.
- Accept arbitrary package feeds from tool callers.
- Provide semantic or vector search in the initial release.
- Provide multi-user HTTP access before shared deployment.

## 4. Users And Workflows

### 4.1 Discover a package

1. An agent calls `resolve_library` with a package name or concept.
2. The server ranks indexed packages.
3. The agent receives one stable `nuget:{environment}/{packageId}` identifier
   per indexed environment and its recommended version.

### 4.2 Retrieve documentation

1. The agent calls `query_docs` with a library ID and concrete question.
2. The server resolves one package version.
3. It returns ranked README, packaged documentation, XML documentation, and
   symbol evidence with citations.

### 4.3 Inspect an API

1. The agent calls `get_symbol` with a type or member name.
2. The server returns the indexed signature, documentation, assembly, target
   frameworks, related members, and citation.
3. For a generated API-client package, this is how the agent finds client
   classes, methods, request models, and response models.

### 4.4 Select a version

The agent calls `list_versions` to see listed, prerelease, deprecated, indexed,
and recommended versions.

## 5. MCP Surface

The public tool surface contains exactly:

- `resolve_library`
- `query_docs`
- `get_symbol`
- `list_versions`

All tools return a structured status, data payload, resolved context where
applicable, evidence, citations, warnings, and errors.

Supported result statuses include:

- `ok`
- `not_found`
- `insufficient_evidence`

## 6. Version Resolution

Resolve versions in this order:

1. Exact requested version.
2. Version supplied from project context.
3. Configured recommended version.
4. Latest stable listed indexed version.
5. Prerelease only when explicitly allowed.

Evidence from different package versions must never be silently combined.
Version parsing and ordering use `NuGet.Versioning`.

## 7. Indexing

### 7.1 Sources

NuGet feeds are configured with:

- A unique name used as both the environment slug and stable source identity.
- NuGet v3 service index URL or approved local package folder.
- Maximum package count.

Package selection is externally provisioned as one top-level JSON file per
package. Each file contains an environment, exact package ID, prerelease and
unlisted policies, and a version limit. Relative package-folder paths resolve
from the Indexer CLI executable directory. Files are loaded once at startup in
filename order and cannot be overridden field-by-field by environment variables
or command-line arguments.

Every package file applies to the feed whose name matches its environment.
Each feed is discovered and atomically published once. A feed with no matching
package files is skipped without deleting existing data. Indexed packages and
versions are deleted only by a matching package file with `Delete` set to
`true`; removing package configuration, removing a package from the feed, or
reducing its version limit does not delete previously indexed data.

### 7.2 Package processing

For each selected package version, index:

- Package ID, semantic version, description, authors, tags, and links.
- Listed, prerelease, deprecation, and publication metadata.
- Dependency groups and target frameworks.
- README and approved Markdown/text artifacts.
- XML documentation.
- Public types and members extracted through metadata-only inspection.

Package code must never be loaded or executed.

### 7.3 Generated API clients

Generated clients use the same indexing path. Their packages should contain:

- A README explaining registration and usage.
- XML documentation for client methods and models.
- Public managed assemblies.
- Any approved packaged examples or supporting documentation.

No generator-specific mapping layer or special package kind is required.

## 8. Search And Ranking

Library resolution ranks:

1. Exact package ID.
2. Package ID prefix or token match.
3. Description, tag, and indexed documentation match.
4. Environment order, source order, and configured-recommendation boosts.

Qualified library IDs restrict retrieval to one environment. Legacy
`nuget:{packageId}` IDs select by environment order and then source order.

Documentation retrieval ranks exact symbol matches above prose, then applies
source-quality and deprecation adjustments. Results must be deterministic for
an unchanged index.

Symbol lookup supports fully qualified, simple, and partial names. Ambiguous
matches return candidates instead of selecting silently.

## 9. Citations And Resources

Stable resource forms include:

```text
nuget://{source}/{packageId}/{version}/artifact/{path}
nuget://{source}/{packageId}/{version}/symbol/{qualifiedName}
```

Resources are read from the local index. Retrieval never contacts the package
feed.

## 10. Persistence

SQLite and FTS5 store:

- Sources and libraries.
- Package versions.
- Artifacts and document chunks.
- Public symbols.
- Dependencies and target frameworks.
- Index-run status and diagnostics.

Source publication is atomic. A failed refresh preserves the previous
successful index.

## 11. Security And Limits

- Treat package archives and documents as untrusted input.
- Enforce archive size, extracted size, entry count, compression ratio, and
  document-size limits.
- Prevent ZIP path traversal.
- Inspect assemblies through metadata APIs only.
- Keep credentials out of configuration output, logs, SQLite evidence, and MCP
  responses.
- Bound source fetch, package download, query duration, result count, response
  size, and indexing concurrency.
- Keep indexed instructions labeled as evidence; they must not alter server
  behavior.

## 12. Architecture

- `Configuration`: Host option contracts and validation.
- `Indexer.Core`: reference-free indexing models, ports, and orchestration.
- `Application`: retrieval handlers, MCP contracts, and policies.
- `Infrastructure`: retrieval adapters plus concrete NuGet, package-processing,
  hashing, and SQLite indexing implementations.
- `Indexer`: one-shot indexing configuration and composition root.
- `Host`: retrieval-only MCP transports, diagnostics, tools, and resources.

Configuration, Indexer Core, and Application have no project references.
Infrastructure references Application and Indexer Core. The Indexer references
Indexer Core and Infrastructure. Host does not register indexing services or contact
package sources.

## 13. Configuration

Host:

```json
{
  "DevContextMcp": {
    "DatabasePath": "data/docs.db",
    "RecommendedVersions": {
      "Company.Customer.Client": "4.2.0",
      "nuget:qa/Company.Customer.Client": "4.3.0-beta.1"
    },
    "Retrieval": {
      "EnvironmentOrder": ["production", "qa"],
      "SourceOrder": ["internal"]
    }
  }
}
```

Indexer CLI:

```json
{
  "DevContextMcp": {
    "DatabasePath": "data/docs.db",
    "NuGetSourcesPath": "nuget-sources",
    "Environments": [
      {
        "Name": "production",
        "ServiceIndex": "https://packages.example/v3/index.json",
        "MaxPackages": 100
      }
    ],
    "Indexing": {
      "MaxPackageBytes": 104857600,
      "PackageDownloadTimeout": "00:02:00"
    }
  }
}
```

Package file `nuget-sources/Company.Customer.Client.json`:

```json
{
  "Environment": "production",
  "PackageId": "Company.Customer.Client",
  "MaxVersionsPerPackage": 3,
  "IncludePrerelease": false,
  "IncludeUnlisted": false
}
```

Setting `Delete` to `true` turns the file into a persistent tombstone that
removes every indexed version of the package from the matching environment
without contacting the feed. Tombstones require only `Environment` and
`PackageId`. This tombstone is the only mechanism that removes indexed NuGet
packages or versions.

## 14. Performance Targets

- Exact symbol lookup p95 below 300 ms.
- Documentation query p95 below 1 second.
- Version listing p95 below 200 ms.
- Default tool responses at or below 100 KB.

Targets are measured against the agreed representative dataset.

## 15. Roadmap

### Stage 1: Skeleton

Establish the solution, MCP stdio host, four tool contracts, configuration,
validation, diagnostics, and test projects.

### Stage 2: NuGet indexing

Build safe package ingestion, metadata and documentation extraction, symbol
inspection, SQLite persistence, FTS5 indexing, and a dedicated Indexer CLI.

### Stage 3: NuGet retrieval

Implement package resolution, version selection, documentation search, symbol
lookup, version listing, citations, and resources.

### Stage 4: Hardening

Add refresh coordination, recovery, limits, security tests, observability,
benchmarks, and agent acceptance scenarios.

### Stage 5: Shared deployment

Add Streamable HTTP, authentication, source-aware authorization, secret
integration, deployment assets, health/readiness, audit logging, and runbooks.

## 16. Product Acceptance

1. A package resolves by exact ID and descriptive concept.
2. Version resolution follows the documented precedence.
3. Documentation evidence is isolated to one package version.
4. Public types and methods return indexed signatures and documentation.
5. A generated API-client package can be resolved and its client methods and
   models can be found through normal package tools.
6. Citations resolve to local indexed resources.
7. Unknown packages and symbols produce explicit statuses without invented
   content.
8. Refresh failures preserve prior successful data.
9. Security and response limits are enforced.
10. Build and test suites pass.
