# Stage 1 Implementation Plan: Application Foundation

## Objective

Create the .NET 10 Minimal API and test projects, register the QA city package,
and deliver the two package-backed city-list endpoints.

## Scope

- Create `Demo.CityApi` and `Demo.CityApi.Tests` under `demo/app`.
- Add both projects to a solution under `demo/app`.
- Reference QA `Demo.Cities` version `1.1.0`.
- Establish API testing with xUnit and the repository's test conventions.
- Configure Problem Details for API errors.
- Implement:
  - `GET /city`
  - `GET /city/usa`

Open-Meteo access and SQLite persistence are deferred to later stages.

## Implementation Steps

1. Create the web and test projects targeting `net10.0`.
2. Add the required project and package references.
3. Register `Demo.Cities` through `AddDemoCities()`.
4. Map `GET /city` to `ICityService.GetCityNames()`.
5. Map `GET /city/usa` to `IUsaCityService.GetCityNames()`.
6. Return the service values directly as JSON arrays without sorting,
   title-casing, or otherwise changing their order or spelling.
7. Enable Problem Details and a centralized exception handler for later
   endpoint errors.
8. Expose the application entry point to the test project when required by
   the selected test host.

## Tests

- `/city` returns `200` and the exact ordered `ICityService` array.
- `/city/usa` returns `200` and the exact ordered `IUsaCityService` array.
- Both responses use JSON string-array shapes.
- Dependency injection resolves both city services.

## Completion Criteria

- Both projects build with warnings treated as errors.
- The two list endpoints satisfy the shared API contract.
- Tests run without network or database access.
- No Open-Meteo or cache implementation is introduced in this stage.
