# Stage 1: USA City Service

## Requirement

Extend only the QA `Demo.Cities` package with an injectable service that
returns a fixed list of major United States cities.

## Public API

```csharp
public interface IUsaCityService
{
    IReadOnlyList<string> GetCityNames();
}
```

Register `IUsaCityService` as a singleton through the existing
`AddDemoCities()` extension.

## Fixed Values

Return Chicago, Houston, Los Angeles, New York, Philadelphia, and Phoenix in
alphabetical order using a read-only collection.

## Acceptance Criteria

- The existing `ICityService` and its values remain unchanged.
- `IUsaCityService` resolves through `AddDemoCities()` with singleton lifetime.
- The returned values are exact, ordered, unique, nonblank, and immutable.
- The QA package builds, tests, and packs successfully as version `1.0.0`.
- The Prod package remains unchanged.
