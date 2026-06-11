# Add QA USA City Service

## Summary
Extend only the QA `Demo.Cities` package with an injectable service returning a fixed, immutable list of major U.S. cities. Leave Prod unchanged.

## Key Changes
- Add public `IUsaCityService`:
  ```csharp
  public interface IUsaCityService
  {
      IReadOnlyList<string> GetCityNames();
  }
  ```
- Add internal `UsaCityService` returning Chicago, Houston, Los Angeles, New York, Philadelphia, and Phoenix in alphabetical order.
- Register `IUsaCityService` as a singleton through the existing `AddDemoCities()` extension.
- Add XML documentation for the interface and update the QA README and specification with registration and usage details.
- Update the Stage 1 BRD with explicit requirements and acceptance criteria.

## Test Plan
- Verify the exact six U.S. cities and their alphabetical order.
- Verify the results contain no duplicates or blank values.
- Verify callers cannot mutate the internal collection.
- Verify `AddDemoCities()` resolves `IUsaCityService` with singleton lifetime while preserving the existing `ICityService` registration.
- Run QA build, tests, and package generation.
- Inspect the QA package for the updated DLL, XML documentation, and README.

## Assumptions
- The existing general `ICityService` and its values remain unchanged.
- Prod intentionally does not receive `IUsaCityService`.
- Package ID and version remain `Demo.Cities` `1.0.0`.
- City values remain plain strings without state, coordinates, or population data.
