# OpenMeteo.Api.Client

`OpenMeteo.Api.Client` is a generated .NET 10 client for the
[Open-Meteo Geocoding API](https://open-meteo.com/en/docs/geocoding-api).
It includes an `IHttpClientFactory` registration extension for applications
that use Microsoft dependency injection.

## Installation

```powershell
dotnet add package OpenMeteo.Api.Client --version 1.0.0
```

## Registration

Register the typed client:

```csharp
using OpenMeteo.Api.Client;

builder.Services.AddOpenMeteoApiClient();
```

The client uses `https://geocoding-api.open-meteo.com` by default.

Configure the managed `HttpClient` when needed:

```csharp
builder.Services.AddOpenMeteoApiClient(httpClient =>
{
    httpClient.Timeout = TimeSpan.FromSeconds(10);
});
```

`AddOpenMeteoApiClient` returns `IHttpClientBuilder`, so handlers and other
HTTP behavior can be added through the standard `HttpClientFactory` APIs.

## Usage

Inject `IOpenMeteoClient` and search for locations:

```csharp
using OpenMeteo.Api.Client;

public sealed class LocationSearch(IOpenMeteoClient client)
{
    public async Task<IReadOnlyList<LocationResult>> SearchAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var response = await client.SearchLocationsAsync(
            name,
            count: 5,
            language: "en",
            format: Format.Json,
            cancellationToken);

        return response.Results.ToArray();
    }
}
```

`GeocodingResponse.Results` contains `LocationResult` values with coordinates,
country, timezone, population, postal codes, and administrative region data.

## Error Handling

Unexpected HTTP responses and invalid response payloads throw `ApiException`:

```csharp
try
{
    var response = await client.SearchLocationsAsync(
        "Chicago",
        count: 5,
        language: "en",
        format: Format.Json,
        cancellationToken);
}
catch (ApiException exception)
{
    Console.WriteLine($"Open-Meteo returned {exception.StatusCode}.");
    Console.WriteLine(exception.Response);
}
```

## Direct Construction

Applications that do not use dependency injection can construct the generated
client directly:

```csharp
using OpenMeteo.Api.Client;

using var httpClient = new HttpClient();
var client = new OpenMeteoClient(httpClient);
```

The generated source is checked in and regenerated explicitly with
`OpenAPI/nswag.sh`; package builds do not run NSwag.
