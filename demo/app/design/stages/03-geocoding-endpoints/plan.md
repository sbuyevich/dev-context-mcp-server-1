# Stage 3 Implementation Plan: Geocoding Endpoints

## Objective

Implement one cache-aside geocoding service and use it for the location and
population endpoints.

## Package Verification

Use DevContext for `OpenMeteo.Api.Client` version `1.0.0` to verify the
registration extension, `IOpenMeteoClient`, search method, response model, and
exception type before implementation.

## City Resolution

1. Trim the route value.
2. Match it case-insensitively against the union of names returned by
   `ICityService` and `IUsaCityService`.
3. Use the matched package value as the canonical city name.
4. Return `404` without a cache lookup or Open-Meteo call when no package name
   matches.

## Cache-Aside Flow

1. Normalize the canonical city name and query SQLite.
2. Return the cached record when present.
3. On a miss, search Open-Meteo with the canonical package-provided name.
4. Select the first result whose name exactly matches the canonical name using
   a case-insensitive comparison.
5. Return `404` when no exact result exists.
6. Persist the selected result, including a nullable population and retrieval
   UTC timestamp.
7. Return the persisted result to either detail endpoint.

Successful records never expire. Location and population requests must share
this service and cache record.

## Endpoint Behavior

- `GET /city/{cityName}/location`
  - returns `200` with `{ "city", "latitude", "longitude" }`
  - returns `404` for unsupported cities or no exact Open-Meteo match
- `GET /city/{cityName}/population`
  - returns `200` with `{ "city", "population" }` when population exists
  - returns `404` when the exact cached result has no population
- Both endpoints return `502` Problem Details when Open-Meteo fails on a cache
  miss.
- A cache hit remains usable when Open-Meteo is unavailable.

## Tests

- New York works through `/city/New%20York/location`.
- Mixed-case and surrounding-whitespace names match their canonical values.
- Names containing spaces are handled after route decoding.
- `New Your` and unsupported names return `404` with no external call.
- Empty and non-exact search results return `404`.
- Duplicate exact results use the first result.
- Location and population responses have the documented JSON properties.
- A result with null population serves location and returns `404` for
  population.
- Repeated location and population requests make one external call in total.
- Open-Meteo failure on a cache miss returns `502`.

## Completion Criteria

- Both detail endpoints satisfy the shared contract.
- All non-success responses use Problem Details JSON.
- No test calls the live Open-Meteo API.
- Cancellation flows from the HTTP request into geocoding and persistence.
