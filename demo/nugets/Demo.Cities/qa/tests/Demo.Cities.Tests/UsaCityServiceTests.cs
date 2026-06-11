using Microsoft.Extensions.DependencyInjection;

namespace Demo.Cities.Tests;

public sealed class UsaCityServiceTests
{
    private static readonly string[] ExpectedCityNames =
    [
        "Chicago",
        "Houston",
        "Los Angeles",
        "New York",
        "Philadelphia",
        "Phoenix"
    ];

    [Fact]
    public void GetCityNamesReturnsExpectedUsaCities()
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

        Assert.Equal(cityNames.Order(StringComparer.Ordinal), cityNames);
        Assert.Equal(cityNames.Count, cityNames.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(cityNames, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void GetCityNamesDoesNotExposeMutableCollection()
    {
        var service = CreateService();
        var cityNames = service.GetCityNames();
        var mutableView = Assert.IsAssignableFrom<IList<string>>(cityNames);

        Assert.Throws<NotSupportedException>(() => mutableView.Add("Seattle"));
        Assert.Equal(ExpectedCityNames, service.GetCityNames());
    }

    [Fact]
    public void AddDemoCitiesRegistersUsaCityServiceAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddDemoCities();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IUsaCityService>();
        var second = provider.GetRequiredService<IUsaCityService>();

        Assert.Same(first, second);
        Assert.NotNull(provider.GetRequiredService<ICityService>());
    }

    private static IUsaCityService CreateService()
    {
        var services = new ServiceCollection();
        services.AddDemoCities();

        return services.BuildServiceProvider().GetRequiredService<IUsaCityService>();
    }
}
