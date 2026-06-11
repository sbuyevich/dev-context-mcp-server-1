using Microsoft.Extensions.DependencyInjection;

namespace Demo.Cities.Tests;

public sealed class CityServiceTests
{
    private static readonly string[] ExpectedCityNames =
    [
        "Berlin",
        "Chicago",
        "London",
        "Paris",
        "Tokyo",
        "Toronto"
    ];

    [Fact]
    public void GetCityNamesReturnsExpectedCities()
    {
        var service = CreateService();

        var cityNames = service.GetCityNames();

        Assert.Equal(ExpectedCityNames, cityNames);
    }

    [Fact]
    public void GetCityNamesReturnsSortedUniqueNonBlankCities()
    {
        var service = CreateService();

        var cityNames = service.GetCityNames();

        Assert.Equal(
            cityNames.Order(StringComparer.Ordinal),
            cityNames);
        Assert.Equal(cityNames.Count, cityNames.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(cityNames, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void GetCityNamesDoesNotExposeMutableCollection()
    {
        var service = CreateService();
        var cityNames = service.GetCityNames();
        var mutableView = Assert.IsAssignableFrom<IList<string>>(cityNames);

        Assert.Throws<NotSupportedException>(() => mutableView.Add("Madrid"));
        Assert.Equal(ExpectedCityNames, service.GetCityNames());
    }

    [Fact]
    public void AddDemoCitiesRegistersCityServiceAsSingleton()
    {
        var services = new ServiceCollection();

        var returnedServices = services.AddDemoCities();

        Assert.Same(services, returnedServices);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ICityService>();
        var second = provider.GetRequiredService<ICityService>();

        Assert.Same(first, second);
    }

    private static ICityService CreateService()
    {
        var services = new ServiceCollection();
        services.AddDemoCities();

        return services.BuildServiceProvider().GetRequiredService<ICityService>();
    }
}
