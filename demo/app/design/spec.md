# Demo City API Specification

## Goal

Create a .NET 10 Minimal API under `demo/app` that exposes package-provided
city lists, retrieves geocoding details from Open-Meteo, and caches successful
results in SQLite.

## API Contract

- `GET /city` returns the `ICityService` names as a JSON string array.
- `GET /city/usa` returns the QA `IUsaCityService` names as a JSON string array.
- `GET /city/{cityName}/location` returns
  `{ "city", "latitude", "longitude" }`.
- `GET /city/{cityName}/population` returns
  `{ "city", "population" }`.
- Detail endpoints accept names from either package-provided city list.
- Match allowed city names case-insensitively after trimming.
- Clients URL-encode city path segments. New York uses
  `/city/New%20York/location`.
- ASP.NET route binding performs URL decoding; application code does not
  decode the value again.
- Unknown or unsupported names return `404` without calling Open-Meteo.
- Empty or non-exact Open-Meteo results return `404`.
- Open-Meteo failures return `502` when no cache entry exists.
- Error responses use Problem Details JSON.

## Shared Requirements

- Create `Demo.CityApi` and xUnit `Demo.CityApi.Tests` projects.
- Use QA `Demo.Cities` `1.1.0`, `OpenMeteo.Api.Client` `1.0.0`,
  `Formula.SimpleRepo` `2.8.1`, and `Microsoft.Data.Sqlite`.
- Register package services through their dependency-injection extensions.
- Verify internal NuGet APIs with DevContext before implementation.
- Normalize cache keys by trimming and converting city names to uppercase.
- Cache successful exact geocoding matches indefinitely.
- Preserve the package-provided order for both city-list endpoints.
- Pass request cancellation tokens through asynchronous data access and
  Open-Meteo calls.
- Tests use temporary SQLite databases and a fake Open-Meteo HTTP handler.
  They never call the live API.

## Delivery Stages

1. [Application Foundation](stages/01-application-foundation/plan.md)
   creates the solution, projects, registrations, shared error behavior, and
   city-list endpoints.
2. [SQLite Cache](stages/02-sqlite-cache/plan.md) defines the cache model,
   schema initialization, and SimpleRepo persistence.
3. [Geocoding Endpoints](stages/03-geocoding-endpoints/plan.md) implements
   validation, Open-Meteo lookup, cache-aside behavior, and both detail
   endpoints.
4. [Verification](stages/04-verification/plan.md) completes integration
   coverage and verifies the full acceptance contract.

Each stage must build on its own and leave all tests from earlier stages
passing.

## Assumptions

- A single geocoding record supplies both coordinates and population.
- Duplicate exact-name Open-Meteo results preserve API order; use the first.
- Exact-name comparison is case-insensitive.
- A match with no population is cached. Its location remains available while
  its population endpoint returns `404`.
- No authentication, pagination, cache administration, expiration, or manual
  refresh endpoint is included.
