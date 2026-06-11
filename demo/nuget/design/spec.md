# Demo.Countries NuGet Specification

## Summary

Create `demo/nuget/spec.md` describing a simple `net10.0` NuGet class library named `Demo.Countries`. The package exposes an injectable service that returns a fixed, alphabetically ordered list of country names without networking or persistence.

## Implementation

- Create `Demo.Countries` and `Demo.Countries.Tests` projects.
- Define:

```csharp
public interface ICountryService
{
    IReadOnlyList<string> GetCountryNames();
}
```

- Implement the service with an immutable hardcoded list: Canada, France, Germany, Japan, United Kingdom, and United States.
- Register it as a singleton through:

```csharp
services.AddDemoCountries();
```

- Use `Demo.Countries` consistently for the package ID, assembly, project, and root namespace.
- Enable nullable reference types, implicit usings, XML documentation, deterministic builds, and NuGet package generation.
- Include package metadata and a README with installation, DI registration, and usage examples.
- Keep the package free of HTTP, configuration, logging, database, and environment-specific behavior.

## Tests

- Verify the exact expected country names are returned.
- Verify names are alphabetically ordered and contain no duplicates or blanks.
- Verify callers cannot mutate the package’s internal collection.
- Verify `AddDemoCountries()` resolves `ICountryService` and uses singleton lifetime.
- Verify `dotnet build`, `dotnet test`, and `dotnet pack` succeed.

## Assumptions

- The package targets only `net10.0`.
- Version starts at `1.0.0`.
- Country names are plain strings rather than models with codes.
- The package is consumed directly as a .NET library, not through an HTTP API.
- The new specification lives at `demo/nuget/spec.md`.
