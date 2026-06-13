# Demo.Cities NuGet Specification

## Summary

Create `demo/nuget/spec.md` describing a simple `net10.0` NuGet class library named `Demo.Cities`. The package exposes an injectable service that returns a fixed, alphabetically ordered list of city names without networking or persistence.

## Implementation

- Create `Demo.Cities` and `Demo.Cities.Tests` projects.
- Define:

```csharp
public interface ICityService
{
    IReadOnlyList<string> GetCityNames();
}
```

- Implement the service with an immutable hardcoded list: Berlin, Chicago, London, Paris, Tokyo, and Toronto.
- Register it as a singleton through:

```csharp
services.AddDemoCities();
```

- Use `Demo.Cities` consistently for the package ID, assembly, project, and root namespace.
- Enable nullable reference types, implicit usings, XML documentation, deterministic builds, and NuGet package generation.
- Include package metadata and a README with installation, DI registration, and usage examples.
- Keep the package free of HTTP, configuration, logging, database, and environment-specific behavior.

## Tests

- Verify the exact expected city names are returned.
- Verify names are alphabetically ordered and contain no duplicates or blanks.
- Verify callers cannot mutate the package’s internal collection.
- Verify `AddDemoCities()` resolves `ICityService` and uses singleton lifetime.
- Verify `dotnet build`, `dotnet test`, and `dotnet pack` succeed.

## Assumptions

- The package targets only `net10.0`.
- Version starts at `1.0.0`.
- City names are plain strings rather than models with coordinates.
- The package is consumed directly as a .NET library, not through an HTTP API.
- The new specification lives at `demo/nuget/spec.md`.
