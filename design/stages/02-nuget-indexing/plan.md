# Stage 2 Implementation Plan: NuGet Indexing

## Objective

Implement the first real indexing pipeline: discover configured NuGet packages, download package archives safely, extract metadata/docs/public symbols without executing package code, and persist the indexed content into SQLite with FTS5.

This stage does not add natural-language retrieval or ranking. It should make the database trustworthy, inspectable, and ready for later MCP query tools.

## Key Decisions

- Keep retrieval layered and indexing vertical:
  - `DevContextMcp.Indexer.Core`: reference-free source-neutral records, ports, and
    orchestration.
  - `DevContextMcp.Server.Core`: retrieval use cases and MCP contracts.
  - `DevContextMcp.Infrastructure`: retrieval adapters plus concrete NuGet,
    package-processing, and SQLite indexing implementations.
  - `DevContextMcp.Indexer`: one-shot configuration and composition root.
  - `DevContextMcp.Server.Configuration`: Host option contracts and validation.
  - `DevContextMcp.Server`: retrieval-only MCP composition and diagnostics.
- Use official NuGet client APIs:
  - `NuGet.Protocol` for source repositories, package search, metadata lookup, and package downloads.
  - `NuGet.Packaging` for `.nupkg` / `.nuspec` archive inspection.
- Use `Microsoft.Data.Sqlite` directly for the index store.
  - Direct SQL is the clearest fit for FTS5 virtual tables, `PRAGMA user_version`, and atomic publish transactions.
- Use metadata-only assembly inspection for public symbols.
  - Prefer `System.Reflection.MetadataLoadContext` for readable public type/member extraction.
  - Use `ref` assemblies before `lib` assemblies when both exist.
  - Never execute package code.
- Keep network indexing opt-in.
  - Default configured NuGet sources remain empty or inert.
  - Stage 2 tests should use local fixture sources/packages.

## Package Updates

Add central package versions in `Directory.Packages.props`:

- `NuGet.Protocol`
- `NuGet.Packaging`
- `Microsoft.Data.Sqlite`
- `System.Reflection.MetadataLoadContext`

Reference implementation packages from Infrastructure, unless a test project
needs fixture helpers directly.

## Configuration

Extend `NuGetSourceOptions`:

- `Name`
- `Environment`
- `ServiceIndex`
- `PackagePrefixes`
- `PackageIds`
- `IncludePrerelease`
- `IncludeUnlisted`
- `MaxVersionsPerPackage`
- `MaxPackages`

Extend `IndexingOptions`:

- `MaxPackageBytes`
- `MaxArchiveEntries`
- `MaxExtractedBytes`
- `MaxCompressionRatio`
- `MaxDocumentChars`
- `PackageDownloadTimeout`

Add validation rules:

- Source names must be unique.
- Every NuGet source name must be a valid environment slug.
- A source must have a `ServiceIndex`.
- A source must define at least one package prefix or explicit package id before indexing runs.
- Size/count/time limits must be positive.

Credential support for private feeds should be represented by an injectable provider interface with an anonymous default implementation. Do not log credentials or place secret-handling behavior directly in this stage.

## Indexing Model

Add source-neutral records/value objects for:

- `PackageSourceRecord`
- `LibraryRecord`
- `LibraryVersionRecord`
- `ArtifactRecord`
- `DocumentChunkRecord`
- `SymbolRecord`
- `DependencyRecord`
- `TargetFrameworkRecord`
- `IndexRunRecord`
- `IndexRunErrorRecord`
- `PackageIdentityKey`

Use normalized deterministic keys where practical:

- Source key from normalized source name/service index.
- Library key from source key + normalized package id.
- Version key from library key + normalized NuGet version.
- Content/artifact hash from SHA-256.

Avoid random identifiers for persisted package identity.

## Indexer Ports

Add Indexer contracts:

- `IIndexCoordinator`
  - Runs indexing for configured sources.
  - Returns an index run summary with counts, status, warnings, and errors.
- `IIndexingSourceProvider`
  - Converts Indexer configuration into source-neutral indexing source
    definitions.
- `IPackageSourceClient`
  - Discovers package identities and selected versions.
  - Downloads package archives into bounded temp files/streams.
- `IPackageProcessor`
  - Converts a downloaded package archive into metadata, documents, symbols, dependencies, and target frameworks.
- `IIndexStore`
  - Initializes/migrates the database.
  - Stages package-version index data.
  - Publishes successful staged changes atomically.
  - Records index run errors.
- `IDocumentChunker`
- `IContentHasher`

Keep these interfaces source-neutral where possible. NuGet-specific details
should live in concrete Indexer implementations or explicit NuGet DTOs.

## NuGet Discovery

Implement `NuGetPackageSourceClient` in the Indexer.

Discovery behavior:

- Explicit package ids:
  - Use `PackageMetadataResource.GetMetadataAsync`.
  - Honor `IncludePrerelease` and `IncludeUnlisted`.
- Prefix discovery:
  - Use `PackageSearchResource.SearchAsync`.
  - Page through results until `MaxPackages` or no more results.
  - Filter returned package ids with ordinal-insensitive `StartsWith` against configured prefixes.
  - Use metadata lookup for selected package ids to choose versions.
- Version selection:
  - Exclude prerelease unless `IncludePrerelease` is true.
  - Exclude unlisted versions unless `IncludeUnlisted` is true.
  - Sort by NuGet version descending.
  - Take `MaxVersionsPerPackage`.

Important limitation:

- Prefix search cannot reliably discover brand-new unlisted packages because unlisted packages are normally excluded from search results. `IncludeUnlisted` applies best to explicit package ids or already-known packages.

## Safe Download And Archive Handling

Download behavior:

- Use `FindPackageByIdResource.CopyNupkgToStreamAsync`.
- Write to a unique temp file or bounded stream.
- Enforce:
  - `MaxPackageBytes`
  - download timeout
  - cancellation token
  - SHA-256 content hash
- Delete temp files in `finally`.

Archive validation:

- Read archive entries without extracting the package to disk.
- Reject entries with:
  - rooted paths
  - `..` traversal segments
  - empty names where a file is expected
  - NUL/control characters
  - excessive compressed/uncompressed size
  - excessive compression ratio
- Enforce:
  - `MaxArchiveEntries`
  - `MaxExtractedBytes`
- Continue the run after a rejected/malformed package by recording an index run error.

## Extraction

Package metadata:

- Use `PackageArchiveReader.NuspecReader`.
- Capture:
  - id
  - version
  - title
  - description
  - summary
  - authors
  - tags
  - project URL
  - license metadata when available
  - repository metadata when available
  - dependencies
  - target frameworks
  - readme path when available

Documents:

- Extract approved package documents:
  - README files
  - `.xml` documentation files
  - selected `.md` and `.txt` docs under safe package paths
- Chunk documents by:
  - markdown headings/paragraphs
  - XML doc member entries
  - bounded fallback text chunks
- Store chunk metadata:
  - package id/version
  - source path
  - document kind
  - symbol/member name when known
  - ordinal
  - content hash

Symbols:

- Inspect assemblies with metadata-only loading.
- Prefer `ref/**/*.dll`; fall back to `lib/**/*.dll`.
- Extract public:
  - namespaces
  - types
  - constructors
  - methods
  - properties
  - events
  - fields
- Capture signatures, containing type, assembly path, target framework, and visibility.
- Skip compiler-generated/internal/private members.
- If a dependency cannot be resolved, record a warning and continue indexing available metadata.

## SQLite Schema

Create migrations using direct SQL and `PRAGMA user_version`.

Initial tables:

- `sources`
- `libraries`
- `library_versions`
- `artifacts`
- `document_chunks`
- `document_chunks_fts`
- `symbols`
- `dependencies`
- `target_frameworks`
- `index_runs`
- `index_run_errors`

Required database behavior:

- Enable `PRAGMA foreign_keys = ON` per connection.
- Prefer WAL mode for normal runtime.
- Use deterministic unique constraints for package identity.
- Use `content_hash` to skip unchanged package data.
- Use FTS5 for searchable document chunks.
- Keep schema creation idempotent.

FTS behavior:

- `document_chunks` remains the canonical table.
- `document_chunks_fts` indexes searchable text plus useful denormalized metadata.
- Updates to FTS should happen in the same publish transaction as canonical rows.

## Incremental And Atomic Indexing

Each indexing invocation creates an `index_runs` row.

Processing flow:

1. Load configured sources.
2. Initialize/migrate SQLite.
3. Discover package ids and versions.
4. For each package version:
   - download safely
   - validate archive
   - extract metadata/docs/symbols
   - compute content hash
   - stage results
   - record package-level failures without stopping the whole source
5. Publish successful staged results in a transaction.
6. Mark run as:
   - `succeeded`
   - `partial_success`
   - `failed`

Failure rules:

- A failed package version must not delete a previously successful index for that version.
- A failed run must not corrupt or partially replace the last good database state.
- A malformed package should create an `index_run_errors` row and allow remaining packages to continue.

## Indexer Integration

Register indexing through separate composition methods:

- `AddIndexer()` registers the index coordinator.
- `AddIndexingInfrastructure()` registers the NuGet source client, package
  processor, document chunker, content hasher, SQLite index store, and
  anonymous credential provider.
- `AddIndexerCli(IConfiguration)` binds CLI configuration and composes both
  registrations.

The Indexer CLI:

- Runs all configured sources once per invocation.
- Returns exit `0` for success or no configured sources and exit `1` for
  failed, partial, invalid, canceled, or unhandled runs.
- Skips quietly when no sources are configured.
- Logs source/run summaries.
- Uses an external scheduler when recurring indexing is required.

Extend diagnostics/status:

- Include database path.
- Include whether SQLite can open/create the configured database.
- Include configured source count.
- Include last index run summary when available.

Keep the MCP Host retrieval-only so indexing cannot delay MCP availability.

## Fixture Strategy

Create local test fixtures under `tests/Fixtures/NuGet`.

Fixture packages should include:

- README
- XML documentation
- at least one public class/method/property
- dependency metadata
- target framework metadata
- prerelease version
- malformed/path-traversal package variant
- oversized or limit-triggering package variant

Prefer building fixture packages during tests with `dotnet pack` when stable. If that becomes too slow or flaky, commit tiny prebuilt `.nupkg` fixtures with clear source projects next to them.

Use a local folder package source for integration tests so Stage 2 does not require network access.

## Tests

Add focused unit tests for:

- option validation
- package id normalization
- version selection
- prefix filtering
- archive path traversal rejection
- archive size/count/ratio limits
- document chunking
- XML doc member chunking
- symbol visibility filtering
- SQLite migration idempotency

Add integration tests for:

- local fixture source indexing succeeds
- package metadata is persisted
- dependencies and target frameworks are persisted
- README/XML docs are written to FTS
- public symbols are persisted
- re-indexing unchanged packages does not duplicate rows
- package failure preserves previous successful records
- malformed package does not stop valid package indexing
- cancellation stops the run cleanly

Verification commands:

```powershell
dotnet build .\DevContextMcp.slnx
dotnet test .\DevContextMcp.slnx
```

## Work Packages

1. Update packages and project references.
2. Add Indexer CLI configuration/options and validation.
3. Add Indexer records and identity normalization helpers.
4. Add Indexer ports and run result contracts.
5. Implement SQLite schema, migrations, and store primitives.
6. Implement NuGet package discovery and version selection.
7. Implement bounded package download and archive validation.
8. Implement metadata/document extraction and chunking.
9. Implement metadata-only public symbol extraction.
10. Implement index coordinator with staged atomic publish.
11. Wire Indexer orchestration, Infrastructure adapters, and the Indexer CLI.
12. Add fixture packages and unit/integration tests.
13. Update README with local indexing/test instructions.
14. Run build/test and fix compile or behavioral failures.

## Acceptance Criteria Mapping

- Configured NuGet packages are discoverable by prefix and explicit package id.
- Prerelease and unlisted behavior is controlled by options.
- Package downloads are bounded, cancellable, hashed, and cleaned up.
- Malicious archive paths and unsafe extraction patterns are rejected.
- Package metadata, dependencies, target frameworks, documents, and public symbols are extracted without executing package code.
- SQLite schema is created idempotently and includes FTS5 document search tables.
- Re-indexing is incremental and does not duplicate records.
- Failed package updates preserve previous successful data.
- Local fixture tests prove indexing without internet access.
- The one-shot Indexer writes the index without involving the MCP Host.

## Risks

- NuGet prefix search may not discover unlisted packages. Use explicit package ids for unlisted indexing.
- Metadata-only symbol extraction can hit assembly resolution gaps. Treat unresolved dependencies as warnings and keep useful public metadata.
- FTS5 availability depends on the SQLite native library. Add an integration test that creates the FTS virtual table early.
- Multiple Indexer CLI processes can contend for one SQLite database. Deploy
  exactly one writer until cross-process coordination is implemented.

## Definition Of Done

- Stage 2 code is implemented in the existing project layout.
- `dotnet build .\DevContextMcp.slnx` passes.
- `dotnet test .\DevContextMcp.slnx` passes.
- README explains how to run fixture indexing locally.
- `design/stages/02-nuget-indexing/brd.md` acceptance criteria are covered by code or tests.
