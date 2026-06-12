# Stage 2 Implementation Plan: SQLite Cache

## Objective

Add an initialized SQLite cache and a repository abstraction capable of
reading and inserting one geocoding record per normalized city name.

## Package Verification

Before writing persistence code, use DevContext for
`Formula.SimpleRepo` version `2.8.1` to verify:

- `RepositoryBase`
- SQLite `ConnectionDetails`
- `GetAsync`
- `InsertAsync`
- entity mapping and parameter conventions
- cancellation-token support

Do not infer signatures that DevContext cannot confirm.

## Cache Model

Store:

- `Id`
- normalized city name
- display city name
- country
- latitude
- longitude
- nullable population
- retrieval UTC timestamp

The normalized city name is the trimmed canonical package name converted to
uppercase. Enforce a unique index on it.

## Implementation Steps

1. Add `Formula.SimpleRepo` `2.8.1` and `Microsoft.Data.Sqlite`.
2. Add configuration for the SQLite connection string or database path.
3. Define the cache entity and repository contract.
4. Implement the repository with the verified SimpleRepo APIs.
5. Use `Microsoft.Data.Sqlite` only to initialize the table and unique index.
6. Make schema initialization idempotent and run it during application
   startup.
7. Implement repository operations to:
   - get a record by normalized city name
   - insert a successful geocoding record
8. Propagate cancellation tokens where the verified package API permits.
9. Treat a duplicate-key race as an opportunity to read the record that
   another request inserted.

## Tests

- Schema initialization succeeds on an empty temporary database.
- Repeated initialization does not fail or recreate data.
- A record round-trips with all fields intact.
- Lookup uses the normalized city key.
- The unique index prevents duplicate normalized names.
- Nullable population is persisted and read as `null`.
- Tests create isolated temporary SQLite databases.

## Completion Criteria

- The cache schema and repository build against verified SimpleRepo APIs.
- Persistence tests pass against real temporary SQLite databases.
- The implementation does not call Open-Meteo.
- Database files created by tests are cleaned up.
