# Demo City API

## Goal

Create a .NET 10 Minimal API that exposes city lists and geocoding details.
Use the internal NuGet packages for city data and Open-Meteo access, and cache
external API results in SQLite.

## Endpoints

- `GET /city`
  - Return all city names from `Demo.Cities.ICityService`.
- `GET /city/usa`
  - Return U.S. city names from the QA-only
    `Demo.Cities.IUsaCityService`.
- `GET /city/{cityName}/location`
  - Return the matched city's latitude and longitude.
- `GET /city/{cityName}/population`
  - Return the matched city's population.

City names must be URL-decoded and matched case-insensitively. Return:

- `200 OK` when data is available.
- `404 Not Found` when the city is not in `Demo.Cities` or Open-Meteo has no
  matching result.
- `502 Bad Gateway` when Open-Meteo fails and no cached result is available.

## External Data

Use `OpenMeteo.Api.Client.IOpenMeteoClient` to retrieve geocoding data. A
single geocoding result supplies the coordinates and population used by both
detail endpoints.

## Caching

Use a cache-aside strategy:

1. Normalize the city name and check SQLite first.
2. Return the cached geocoding result when present.
3. On a cache miss, call Open-Meteo and persist the selected result.
4. Reuse the cached result for subsequent location and population requests.

Store at least the normalized city name, display name, country, latitude,
longitude, population, and retrieval timestamp. Enforce one cache record per
normalized city name.

Use `SimpleRepo` for SQLite data access. Verify its public API through
DevContext before implementation; do not infer unavailable package APIs.

## Dependencies

- Target .NET 10.
- Prefer QA versions of internal NuGet packages when available.
- Use the QA `Demo.Cities` package because it provides `IUsaCityService`.
- Use `OpenMeteo.Api.Client` for Open-Meteo geocoding requests.
- Register package services through their dependency-injection extensions.

## Acceptance Criteria

- All four endpoints return JSON and follow the documented status codes.
- `/city` and `/city/usa` preserve the package-provided alphabetical order.
- The location and population endpoints share the same cached geocoding
  record.
- Repeating either detail request for a cached city does not call Open-Meteo.
- Automated tests cover city lists, successful lookups, cache hits, missing
  cities, empty Open-Meteo results, and upstream failures.
