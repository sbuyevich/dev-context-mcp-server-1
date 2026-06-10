# MCP Documentation Server

A .NET 10 Model Context Protocol server for helping coding agents discover and
correctly use internal NuGet packages, including generated API-client packages.

Stage 3 adds version-aware NuGet retrieval over the SQLite/FTS5 index. The
server can resolve packages, select versions, query README and XML
documentation, inspect public symbols, and return stable citations without
executing package code.

## Prerequisites

- .NET SDK 10.0.204 or a compatible 10.0 patch selected by `global.json`.

## Build and test

```powershell
dotnet restore .\DevContextMcp.slnx
dotnet build .\DevContextMcp.slnx --no-restore
dotnet test .\DevContextMcp.slnx --no-build --no-restore
```

## Run the Indexer CLI

The Indexer CLI is the only process that writes the index. Each invocation
indexes all configured sources once and exits:

```powershell
dotnet run --project .\src\DevContextMcp.Indexer.Cli\DevContextMcp.Indexer.Cli.csproj
```

It exits `0` after a successful run or when no sources are configured. It exits
`1` after a failed, partially successful, invalid, or canceled run. Use an
external scheduler for recurring indexing, and run only one Indexer CLI process
against a SQLite database at a time.

## Run over stdio

```powershell
dotnet run --project .\src\DevContextMcp.Server\DevContextMcp.Server.csproj
```

Standard output is reserved for MCP protocol messages. Application and startup
logs are written to standard error.

Example MCP client configuration:

```json
{
  "mcpServers": {
    "internal-docs": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\mcp-doc-server\\src\\DevContextMcp.Server\\DevContextMcp.Server.csproj"
      ]
    }
  }
}
```

For a client launched outside the repository, use an absolute project path.

## Run over Streamable HTTP

Start the same host in local HTTP mode:

```powershell
dotnet run --project .\src\DevContextMcp.Server\DevContextMcp.Server.csproj `
  -- --DevContextMcp:Transport=http
```

Connect a Streamable HTTP MCP client to:

```text
http://127.0.0.1:5034/mcp
```

HTTP mode is stateless and intentionally unauthenticated for local development.
Configuration validation restricts it to a loopback HTTP URL. Shared-network
binding, TLS, authentication, authorization, and CORS remain Stage 5 work.

## Test with MCP Inspector

For stdio, launch Inspector from the repository root:

```powershell
npx -y @modelcontextprotocol/inspector dotnet run --project .\src\DevContextMcp.Server\DevContextMcp.Server.csproj
```

The host loads `appsettings.json` from its executable directory, independent of
the caller's working directory. Configure an absolute `DatabasePath` when the
index should live outside the repository.

To inspect HTTP mode, start the server separately with the HTTP command above,
select **Streamable HTTP** in Inspector, and connect to
`http://127.0.0.1:2222/mcp`.

After connecting:

1. Open **Tools** and call `resolve_library` with an indexed package ID.
2. Pass the returned `nuget:{environment}/{packageId}` value to
   `list_versions`.
3. Call `query_docs` or `get_symbol` with a concrete version.
4. Open **Resources** to read cited artifact and symbol URIs.

## Tools

- `resolve_library`: find indexed NuGet packages by ID or descriptive text,
  optionally within one environment.
- `query_docs`: retrieve ranked, version-isolated documentation evidence.
- `get_symbol`: inspect a public type or member, including XML documentation.
- `list_versions`: list indexed semantic versions and the recommendation.

The tools return structured `ok`, `not_found`, or `insufficient_evidence`
results.

## Resources

Tool citations can be read through these MCP resource templates:

```text
nuget://{source}/{packageId}/{version}/artifact/{path}
nuget://{source}/{packageId}/{version}/symbol/{qualifiedName}
```

Resources are served only from the local index; retrieval never contacts the
NuGet feed.

## Configuration

The Host and Indexer CLI have separate `appsettings.json` files. Both use the
`DevContextMcp` root section and standard .NET environment-variable and
command-line overrides.

Host configuration:

```json
{
  "DevContextMcp": {
    "Transport": "stdio",
    "Http": {
      "Url": "http://127.0.0.1:2222",
      "Path": "/mcp"
    },
    "DatabasePath": "data/docs.db",
    "RecommendedVersions": {
      "Company.Foundation": "4.0.0",
      "nuget:qa/Company.Foundation": "4.1.0-beta.1"
    },
    "Retrieval": {
      "EnvironmentOrder": [ "production", "qa" ],
      "SourceOrder": [ "internal" ],
      "DefaultMaxResults": 8,
      "MaxResults": 25,
      "MaxResponseBytes": 102400,
      "QueryTimeout": "00:00:05",
      "MinimumEvidenceScore": 0.15,
      "AmbiguousSymbolLimit": 10
    }
  }
}
```

Indexer CLI configuration:

```json
{
  "DevContextMcp": {
    "DatabasePath": "data/docs.db",
    "NuGetSourcesPath": "nuget-sources",
    "Environments": [
      {
        "Environment": "production",
        "Name": "internal",
        "ServiceIndex": "https://packages.example/v3/index.json",
        "MaxPackages": 100
      }
    ],
    "RepositorySources": [],
    "Indexing": {
      "MaxPackageBytes": 104857600,
      "MaxDocumentBytes": 20971520,
      "MaxArchiveEntries": 10000,
      "MaxExtractedBytes": 524288000,
      "MaxCompressionRatio": 200,
      "MaxDocumentChars": 4000,
      "PackageDownloadTimeout": "00:02:00"
    }
  }
}
```

Create one top-level JSON file per package in the external
`nuget-sources` folder:

```json
{
  "Environment": "production",
  "PackageId": "Company.Foundation",
  "MaxVersionsPerPackage": 3,
  "IncludePrerelease": false,
  "IncludeUnlisted": false
}
```

### Configuration loading and overrides

The Host and Indexer CLI load their own `appsettings.json` from the executable
directory. This makes JSON configuration independent of the directory from
which the executable was launched.

Environment variables use double underscores between configuration segments:

```powershell
$env:DevContextMcp__DatabasePath = "C:\mcp-doc-server\data\docs.db"
$env:DevContextMcp__Retrieval__EnvironmentOrder__0 = "production"
```

Command-line arguments use colon-separated paths:

```powershell
dotnet run --project .\src\DevContextMcp.Server\DevContextMcp.Server.csproj `
  -- --DevContextMcp:Transport=http `
     --DevContextMcp:Http:Url=http://127.0.0.1:5034
```

Environment variables and command-line arguments override JSON values.
Collection entries use a zero-based index. `TimeSpan` values use `hh:mm:ss`,
so `00:02:00` means two minutes. Invalid values fail startup rather than
silently falling back. Overrides may change `Environments` and
`NuGetSourcesPath`; package policy fields are owned only by the external JSON
files.

Configuration is read when the process starts. Restart the Host or rerun the
Indexer CLI after changing its `appsettings.json` or package files.

### Host values

| Setting | Meaning and rules |
| --- | --- |
| `Transport` | Selects one MCP transport for the process. Allowed lowercase values are `stdio` and `http`. The default is `stdio`. |
| `Http:Url` | Base address used in HTTP mode. It must be an absolute `http://` loopback URL such as `http://127.0.0.1:5034`, with no path, query, fragment, or credentials. It is validated even when `Transport` is `stdio`. |
| `Http:Path` | Streamable HTTP endpoint path mapped under `Http:Url`. It must start with `/` and cannot contain a query or fragment. The default is `/mcp`. |
| `DatabasePath` | SQLite index opened read-only by the Host. It must point to the same database written by the Indexer. Relative paths are resolved from the process working directory, so an absolute path is recommended. |
| `RecommendedVersions` | Optional package-to-version map. Use `Company.Package` for a package-wide recommendation or `nuget:qa/Company.Package` for an environment-specific recommendation. Keys are case-insensitive and values must be semantic versions such as `2.8.1` or `2.9.0-beta.1`. |
| `Retrieval:EnvironmentOrder` | Case-insensitive precedence for legacy IDs such as `nuget:Company.Package`. Each value must be a slug containing only letters, numbers, `.`, `_`, or `-`. Environments not listed here sort after listed environments by ordinal name. |
| `Retrieval:SourceOrder` | Case-insensitive feed precedence within an environment. Source names not listed here sort after listed sources by ordinal name. Empty and duplicate values are rejected. |
| `Retrieval:DefaultMaxResults` | Configured default result count. It must be positive and no greater than `MaxResults`. The current `query_docs` wire contract independently defaults `MaxResults` to `8`, so keep these values aligned. |
| `Retrieval:MaxResults` | Hard upper bound for requested result counts. It caps `query_docs` evidence and `resolve_library` matches. It must be positive and at least `DefaultMaxResults`. |
| `Retrieval:MaxResponseBytes` | Maximum combined UTF-8 byte count of evidence text selected for a `query_docs` response. It does not include the surrounding JSON, metadata, or citations. Extra evidence is omitted with a truncation warning. |
| `Retrieval:QueryTimeout` | Maximum duration of each retrieval operation, including SQLite queries. A timeout returns a structured tool error instead of waiting indefinitely. |
| `Retrieval:MinimumEvidenceScore` | Lowest accepted `query_docs` relevance score, from `0` through `1`. Higher values return less, stronger evidence; lower values accept weaker matches. |
| `Retrieval:AmbiguousSymbolLimit` | Maximum number of symbol candidates returned when `get_symbol` cannot select one unambiguous symbol. It must be positive. |

### Indexer root values

| Setting | Meaning and rules |
| --- | --- |
| `DatabasePath` | SQLite index created and updated by the Indexer CLI. Use exactly the same path as the Host. Relative paths are resolved from the process working directory. |
| `NuGetSourcesPath` | External folder containing top-level package JSON files. Relative paths resolve from the Indexer CLI executable directory. The folder must exist and is not copied by the project. Files are loaded once at startup in filename order. |
| `Environments` | NuGet feeds or local package folders to index. Multiple feeds may share an environment. The collection may be empty; the Indexer CLI then succeeds without doing work. |
| `RepositorySources` | Reserved configuration for planned repository indexing. Entries are validated, but the current Indexer CLI indexes only configured NuGet environments; leave this as `[]`. |
| `Indexing` | Download, archive-safety, and document-processing limits described below. |

### NuGet environment values

| Setting | Meaning and rules |
| --- | --- |
| `Name` | Stable, human-readable feed identity such as `nuget.org` or `internal-qa`. It appears in citations and `SourceId`. Names must be non-empty and unique, case-insensitively, across NuGet and repository sources. |
| `Environment` | Selection label such as `production`, `qa`, or `public`. It is required, compared case-insensitively, and may contain only letters, numbers, `.`, `_`, or `-`. Multiple feeds may share an environment. |
| `ServiceIndex` | Absolute HTTP/HTTPS NuGet v3 service-index URL, or a local package-folder path. Relative local paths are resolved from the Indexer CLI working directory; use an absolute path for predictable deployments. |
| `MaxPackages` | Maximum package files that may match this feed's environment. It must be positive; exceeding it fails startup. |

### NuGet package-file values

| Setting | Meaning and rules |
| --- | --- |
| `Environment` | Environment whose package policy is applied to every matching feed. It must reference a configured environment slug. |
| `PackageId` | Exact package ID to index. It is required and must be unique case-insensitively within its environment. |
| `IncludePrerelease` | When `true`, discovery and metadata selection may include prerelease versions. Retrieval still requires its request-level `IncludePrerelease` flag before selecting a prerelease by fallback or recommendation. |
| `IncludeUnlisted` | When `true`, metadata selection may include unlisted versions. |
| `MaxVersionsPerPackage` | Maximum newest versions retained for each package after prerelease and unlisted filters. It must be positive. There is no unlimited value; use a sufficiently large number when all available versions are required. Lowering it prunes versions outside the retained set on the next successful feed publication. |

Each feed is indexed and published once with all package files matching its
environment. A feed with no matching files is skipped without pruning existing
data.

### Repository source values

| Setting | Meaning and rules |
| --- | --- |
| `Name` | Unique source identity shared with the NuGet source namespace. |
| `RootPath` | Valid local repository root path. This is validated but not indexed by the current Indexer. |

### Indexing values

| Setting | Meaning and rules |
| --- | --- |
| `MaxPackageBytes` | Maximum downloaded `.nupkg` size in bytes. A package exceeding the limit fails before indexing. |
| `MaxDocumentBytes` | Maximum uncompressed size of each README, text, or XML documentation entry read from a package. |
| `MaxArchiveEntries` | Maximum number of ZIP entries allowed in one `.nupkg`. |
| `MaxExtractedBytes` | Maximum total declared uncompressed size across the archive. It also limits each selected managed assembly read. |
| `MaxCompressionRatio` | Maximum allowed uncompressed-to-compressed ratio for an archive entry. This protects against highly compressed archive bombs. |
| `MaxDocumentChars` | Maximum character count of each searchable documentation chunk. Larger documents are split near a natural text boundary. |
| `PackageDownloadTimeout` | Per-package timeout covering package download, flush, and content hashing. |

All indexing limits and timeouts must be positive.

### Environment and version selection

Discovery returns one qualified match per package and environment:

```text
nuget:qa/Company.Foundation
nuget:production/Company.Foundation
```

A qualified ID searches only its named environment and never falls back to
another one. A legacy ID such as `nuget:Company.Foundation` uses
`EnvironmentOrder`, then `SourceOrder` when candidates are otherwise equally
usable. Unlisted order values are still eligible; they sort after configured
values.

Version selection uses this precedence:

1. Exact `Version` supplied by the tool request.
2. Exact `ProjectVersion` supplied as project context.
3. Environment-qualified `RecommendedVersions` entry.
4. Package-wide `RecommendedVersions` entry.
5. Latest indexed, listed stable version.
6. Latest indexed, listed prerelease when the request allows prereleases.

The Indexer CLI must first index a version before the Host can select it.

### Paths, upgrades, and credentials

Give both processes the same preferably absolute `DatabasePath`. Although the
JSON file is loaded from the executable directory, relative database and local
feed paths are resolved from the process working directory.
`NuGetSourcesPath` is the exception: relative values resolve from the Indexer
CLI executable directory.

After upgrading an existing database, run the Indexer CLI before the Host so it
can apply the current schema migration.

On a successful run, the database contains package versions, artifacts,
dependencies, target frameworks, public symbols, document chunks, FTS5 rows,
and index-run history. Re-indexing an unchanged archive skips rewriting it. A
failed package update preserves the prior successful version.

Do not place feed credentials or API tokens in these configuration objects.
Future source integrations will obtain secrets from approved credential
providers.

Generated API clients require no special configuration. Publish them as NuGet
packages with a README, XML documentation, and public assemblies, then include
one package-policy JSON file for each exact package ID.

## Design

- [Product specification](design/spec.md)
- [Stage 1 BRD](design/stages/01-skeleton/brd.md)
- [Stage 1 implementation plan](design/stages/01-skeleton/plan.md)
- [Stage 2 BRD](design/stages/02-nuget-indexing/brd.md)
- [Stage 2 implementation plan](design/stages/02-nuget-indexing/plan.md)
- [Stage 3 BRD](design/stages/03-nuget-retrieval/brd.md)
- [Stage 3 implementation plan](design/stages/03-nuget-retrieval/plan.md)
- [Stage 4 BRD](design/stages/04-hardening/brd.md)
- [Stage 5 BRD](design/stages/05-shared-deployment/brd.md)
