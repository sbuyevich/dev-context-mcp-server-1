# Server Configuration

## appsettings.json

```json
"DevContextMcp": {
   "DatabasePath": "../../../../../database/docs.db",
    "Transport": "http",
    "Http": {
      "Url": "http://127.0.0.1:2222",
      "Path": "/mcp"
    },
    "Retrieval": {
      "EnvironmentOrder": [
        "public"
      ],
      "SourceOrder": [
        "public"
      ],
      "DefaultMaxResults": 8,
      "MaxResults": 25,
      "MaxResponseBytes": 102400,
      "QueryTimeout": "00:00:05",
      "MinimumEvidenceScore": 0.15,
      "AmbiguousSymbolLimit": 10
    },
    "ToolLogging": {
      "MaxPayloadBytes": 32768
    }
  },
```

where:

- `DatabasePath`: path to the SQLite database file shared with the indexer. Must be the same database the indexer writes to.

- `Transport`: transport protocol for MCP communication. Options:
  - `"http"` — Streamable HTTP (default)
  - `"stdio"` — Standard input/output

- `Http`: HTTP transport configuration (only used when `Transport` is `"http"`)
  - `Url`: server address (e.g., `"http://127.0.0.1:2222"`). Use loopback for local development only.
  - `Path`: HTTP endpoint path (e.g., `"/mcp"`). Full address becomes `Url + Path`.

- `Retrieval`: behavior configuration for documentation and symbol queries
  - `EnvironmentOrder`: ordered list of environment names for fallback lookup when no environment is specified. First environment in the list is the default.
  - `SourceOrder`: ordered list of NuGet source names for fallback lookup when no source is specified.
  - `DefaultMaxResults`: default number of results returned by `query_docs` (default: 8).
  - `MaxResults`: maximum number of results allowed in any query response (default: 25).
  - `MaxResponseBytes`: maximum total response size in bytes before truncation (default: 102400).
  - `QueryTimeout`: maximum time allowed for a single query operation (e.g., `"00:00:05"` for 5 seconds).
  - `MinimumEvidenceScore`: minimum relevance score (0.0–1.0) to include search results (default: 0.15).
  - `AmbiguousSymbolLimit`: maximum number of symbol candidates to return when a symbol lookup is ambiguous (default: 10).

- `ToolLogging`: diagnostic logging configuration
  - `MaxPayloadBytes`: maximum size of request/response payloads to include in logs. Larger payloads are truncated (default: 32768).
