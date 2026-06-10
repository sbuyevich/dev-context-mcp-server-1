# Stage 1 BRD: Skeleton

## Purpose

Establish a buildable .NET solution and working MCP server shell with the
boundaries and contracts needed by later stages.

## Business Outcome

A developer can run the server locally, connect an MCP client over stdio, and
discover the four planned NuGet documentation tools.

## Dependencies

None.

## In Scope

- Create the .NET 10 solution and project structure.
- Configure the official Model Context Protocol C# SDK.
- Host the server over stdio with logs on standard error.
- Register application services through dependency injection.
- Define configuration objects and startup validation.
- Define MCP tool input and output contracts.
- Add local startup diagnostics.
- Establish unit and integration test projects.

## Functional Requirements

### FR-1: Solution structure

The solution contains:

```text
src/
  DevContextMcp.Server/
  DevContextMcp.Server.Core/
  DevContextMcp.Indexer/
  DevContextMcp.Indexer.Cli/
  DevContextMcp.Infrastructure/
tests/
  DevContextMcp.UnitTests/
  DevContextMcp.IntegrationTests/
```

Application, Configuration, and Indexer must not have project references.
Infrastructure depends on Application and Indexer. The Indexer CLI depends on
Indexer and Infrastructure.

### FR-2: MCP hosting

The host must:

- Start over stdio.
- Write logs to standard error.
- Register tools through the MCP SDK and dependency injection.
- Shut down cleanly when cancellation is requested.

### FR-3: Tool contracts

Define stable request and response models for:

- `resolve_library`
- `query_docs`
- `get_symbol`
- `list_versions`

Each response supports a machine-readable status, resolved source and version
information where applicable, evidence, citations, warnings, and errors.

### FR-4: Configuration

Define and validate:

- Database location.
- NuGet sources.
- Recommended package versions.
- Indexing safety and processing limits.

Invalid required configuration must fail startup with a useful error.

### FR-5: Diagnostics

The host must confirm:

- The process started.
- Configuration is valid.
- All four tools are registered.
- Required local dependencies are available.

## Non-Functional Requirements

- Nullable reference types and analyzers are enabled.
- Asynchronous operations accept cancellation.
- Tool contracts serialize with `System.Text.Json`.
- Build output is warning-free.
- Normal skeleton startup performs no source access.

## Deliverables

- Buildable solution and projects.
- MCP stdio host.
- Four discoverable tool definitions.
- Configuration and validation models.
- Initial test infrastructure.
- Local run instructions.

## Acceptance Criteria

1. `dotnet build` succeeds.
2. `dotnet test` succeeds.
3. An MCP client starts the process over stdio.
4. The client discovers all four tool names and schemas.
5. Logs are emitted to standard error without corrupting MCP messages.
6. Invalid configuration causes a clear startup validation failure.

## Out of Scope

- NuGet feed access.
- Database schema and indexing.
- Retrieval and ranking.
- HTTP transport and authentication.

## Exit Gate

Stage 2 may begin when the solution builds, tests pass, and four-tool discovery
works reliably over stdio.
