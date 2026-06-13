using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace OpenMeteo.Api.Client.Tests;

public sealed class OpenMeteoClientTests
{
    [Fact]
    public void AddOpenMeteoApiClientRegistersTypedClient()
    {
        var services = new ServiceCollection();

        var builder = services.AddOpenMeteoApiClient();

        Assert.NotNull(builder);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IOpenMeteoClient>();

        Assert.IsType<OpenMeteoClient>(client);
    }

    [Fact]
    public void AddOpenMeteoApiClientAppliesHttpClientConfiguration()
    {
        var expectedTimeout = TimeSpan.FromSeconds(10);
        HttpClient? configuredClient = null;
        var services = new ServiceCollection();
        services.AddOpenMeteoApiClient(client =>
        {
            client.Timeout = expectedTimeout;
            configuredClient = client;
        });

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOpenMeteoClient>();

        Assert.NotNull(configuredClient);
        Assert.Equal(expectedTimeout, configuredClient.Timeout);
    }

    [Fact]
    public async Task SearchLocationsAsyncSendsExpectedRequestAndDeserializesResponse()
    {
        var handler = new StubHttpMessageHandler(
            """
            {
              "results": [
                {
                  "id": 4887398,
                  "name": "Chicago",
                  "latitude": 41.85003,
                  "longitude": -87.65005,
                  "country_code": "US",
                  "timezone": "America/Chicago",
                  "country": "United States"
                }
              ],
              "generationtime_ms": 0.42
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new OpenMeteoClient(httpClient);

        var response = await client.SearchLocationsAsync(
            "Chicago",
            5,
            "en",
            Format.Json);

        Assert.Equal(HttpMethod.Get, handler.Request?.Method);
        Assert.Equal(
            "https://geocoding-api.open-meteo.com/v1/search?name=Chicago&count=5&language=en&format=json",
            handler.Request?.RequestUri?.AbsoluteUri);

        var result = Assert.Single(response.Results);
        Assert.Equal("Chicago", result.Name);
        Assert.Equal("US", result.Country_code);
        Assert.Equal(41.85003, result.Latitude);
        Assert.Equal(0.42, response.Generationtime_ms);
    }

    [Fact]
    public async Task SearchLocationsAsyncThrowsApiExceptionForErrorResponse()
    {
        const string responseBody = """{"reason":"invalid request"}""";
        var handler = new StubHttpMessageHandler(
            responseBody,
            HttpStatusCode.BadRequest);
        using var httpClient = new HttpClient(handler);
        var client = new OpenMeteoClient(httpClient);

        var exception = await Assert.ThrowsAsync<ApiException>(
            () => client.SearchLocationsAsync("Chicago", null, null!, null));

        Assert.Equal((int)HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(responseBody, exception.Response);
    }

    [Fact]
    public async Task SearchLocationsAsyncObservesCancellation()
    {
        var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var client = new OpenMeteoClient(httpClient);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchLocationsAsync(
                "Chicago",
                null,
                null!,
                null,
                cancellation.Token));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public StubHttpMessageHandler(
            string responseBody = "{}",
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;

            return Task.FromResult(
                new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(
                        _responseBody,
                        System.Text.Encoding.UTF8,
                        "application/json")
                });
        }
    }
}
