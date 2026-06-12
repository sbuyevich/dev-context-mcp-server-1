# Demo.Cities

`Demo.Cities` is a small .NET 10 package that provides an injectable service
for reading a fixed, alphabetically ordered list of city names.

The package has no network, configuration, logging, database, or environment
dependencies.

## Installation

```powershell
dotnet add package Demo.Cities --version 1.0.0
```

## Registration

Register the service with Microsoft dependency injection:

```csharp
using Demo.Cities;

services.AddDemoCities();
```

`AddDemoCities()` registers `ICityService` and the QA-only `IUsaCityService`
with singleton lifetime.

## Usage

```csharp
using Demo.Cities;

public sealed class CitySelector(ICityService cityService)
{
    public IReadOnlyList<string> GetOptions() =>
        cityService.GetCityNames();
}
```

The service returns Berlin, Chicago, London, Paris, Tokyo, and Toronto. The
returned collection is read-only.

## United States Cities

The QA package also provides a fixed list of major United States cities:

```csharp
using Demo.Cities;

public sealed class UsaCitySelector(IUsaCityService usaCityService)
{
    public IReadOnlyList<string> GetOptions() =>
        usaCityService.GetCityNames();
}
```

`IUsaCityService` returns Chicago, Houston, Los Angeles, New York,
Philadelphia, and Phoenix in alphabetical order. The returned collection is
read-only.
