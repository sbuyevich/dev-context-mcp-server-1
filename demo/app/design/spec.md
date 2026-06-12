# Demo City API Specification

## Summary

Replace the empty `demo/app/design/spec.md` with an implementation-ready specification for the complete .NET 10 Minimal API described in `idea.md`.

## API Contract

- `GET /city` returns the `ICityService` names as a JSON string array.
- `GET /city/usa` returns the QA `IUsaCityService` names as a JSON string array.
- `GET /city/{cityName}/location` returns `{ "city", "latitude", "longitude" }`.
- `GET /city/{cityName}/population` returns `{ "city", "population" }`.
- Clients URL-encode city path segments. New York uses `/city/New%20York/location`.
- Match allowed city names case-insensitively after trimming.
- `New Your` and other unknown names return `404` without calling Open-Meteo.
- Errors use Problem Details JSON.
- Open-Meteo failures return `502` when no cache entry exists.

## Implementation Requirements

- Create `Demo.CityApi` and xUnit `Demo.CityApi.Tests` projects under `demo/app`.
- Use QA `Demo.Cities` `1.1.0`, `OpenMeteo.Api.Client` `1.0.0`, `Formula.SimpleRepo` `2.8.1`, and `Microsoft.Data.Sqlite`.
- Search Open-Meteo with the canonical package-provided city name and select the first case-insensitive exact-name result.
- Cache results indefinitely using normalized uppercase city names.
- Store `Id`, normalized and display names, country, coordinates, nullable population, and retrieval UTC timestamp in a uniquely indexed SQLite table.
- Implement persistence with `Formula.SimpleRepo.RepositoryBase`, SQLite `ConnectionDetails`, and `GetAsync`/`InsertAsync`; use `Microsoft.Data.Sqlite` only for schema initialization.
- Share one cache-aside geocoding service between location and population endpoints.
- Cache exact geocoding matches even when population is absent; location remains available while population returns `404`.

## Test Plan

- Verify exact ordered arrays for `/city` and `/city/usa`.
- Verify `/city/New%20York/location`, mixed-case names, and names containing spaces.
- Verify success response JSON for location and population.
- Verify typo and unsupported-city requests return `404` without external calls.
- Verify empty or non-exact Open-Meteo results return `404`.
- Verify repeated location/population requests share one cached result and make one external call.
- Verify nullable population behavior and `502` upstream failures.
- Use temporary SQLite databases and a fake Open-Meteo HTTP handler; never call the live API.

## Assumptions

- Successful cache records never expire.
- Duplicate exact-name Open-Meteo results preserve API order and use the first.
- ASP.NET route binding performs URL decoding; application code does not decode the value again.
- No authentication, pagination, cache administration, or manual refresh endpoint is included.
