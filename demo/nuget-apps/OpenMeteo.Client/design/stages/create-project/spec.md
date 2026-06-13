# OpenMeteo NuGet Package

## Summary
Create a `net10.0` NuGet package named `OpenMeteo.Api.Client` around the generated NSwag client, including first-class dependency-injection registration and packaged usage documentation.

## Implementation
- Add library and xUnit test projects under `demo/nugets/OpenMeteo.Client`.
- Configure version `1.0.0`, MIT license, deterministic builds, XML documentation, package README, and package-on-build.
- Compile the checked-in `OpenAPI/Generated/OpenMeteoClient.cs` without relocating it.
- Add centrally managed dependencies on `Newtonsoft.Json` `13.0.3` and `Microsoft.Extensions.Http` `10.0.8`.
- Keep client regeneration explicit through `OpenAPI/nswag.sh`, not part of every build.

## Public API
Add an extension that registers the generated client through `IHttpClientFactory`:

```csharp
services.AddOpenMeteoApiClient();
```

It will:

- Register `IOpenMeteoClient` as a typed HTTP client.
- Construct `OpenMeteoClient` with the managed `HttpClient`.
- Use `https://geocoding-api.open-meteo.com` by default.
- Return `IHttpClientBuilder` so consumers can configure handlers, resilience, logging, and timeouts.

Also provide configuration:

```csharp
services.AddOpenMeteoApiClient(httpClient =>
{
    httpClient.Timeout = TimeSpan.FromSeconds(10);
});
```

Consumers can then inject `IOpenMeteoClient` directly.

## Documentation
Package a README covering:

- NuGet installation
- `AddOpenMeteoApiClient()` registration
- Optional `HttpClient` configuration
- Constructor-based injection of `IOpenMeteoClient`
- Location search and cancellation
- Response models and `ApiException` handling
- Direct construction for applications that do not use DI

Generate and package XML documentation for IntelliSense.

## Test Plan
- Verify DI registration resolves `IOpenMeteoClient` as `OpenMeteoClient`.
- Verify configured `HttpClient` settings are applied.
- Use a fake HTTP handler to test request paths, query parameters, response deserialization, cancellation, and error responses without calling Open-Meteo.
- Run `dotnet build`, `dotnet test`, and `dotnet pack`.
- Inspect the package for the DLL, XML documentation, README, and declared dependencies.
