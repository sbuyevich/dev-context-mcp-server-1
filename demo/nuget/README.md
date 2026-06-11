# Demo.Countries

`Demo.Countries` is a small .NET 10 package that provides an injectable service
for reading a fixed, alphabetically ordered list of country names.

The package has no network, configuration, logging, database, or environment
dependencies.

## Installation

```powershell
dotnet add package Demo.Countries --version 1.0.0
```

## Registration

Register the service with Microsoft dependency injection:

```csharp
using Demo.Countries;

services.AddDemoCountries();
```

`AddDemoCountries()` registers `ICountryService` with singleton lifetime.

## Usage

```csharp
using Demo.Countries;

public sealed class CountrySelector(ICountryService countryService)
{
    public IReadOnlyList<string> GetOptions() =>
        countryService.GetCountryNames();
}
```

The service returns Canada, France, Germany, Japan, United Kingdom, and United
States. The returned collection is read-only.
