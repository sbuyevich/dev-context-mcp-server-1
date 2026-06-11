using Microsoft.Extensions.DependencyInjection;

namespace Demo.Countries.Tests;

public sealed class CountryServiceTests
{
    private static readonly string[] ExpectedCountryNames =
    [
        "Canada",
        "France",
        "Germany",
        "Japan",
        "United Kingdom",
        "United States"
    ];

    [Fact]
    public void GetCountryNamesReturnsExpectedCountries()
    {
        var service = CreateService();

        var countryNames = service.GetCountryNames();

        Assert.Equal(ExpectedCountryNames, countryNames);
    }

    [Fact]
    public void GetCountryNamesReturnsSortedUniqueNonBlankCountries()
    {
        var service = CreateService();

        var countryNames = service.GetCountryNames();

        Assert.Equal(
            countryNames.Order(StringComparer.Ordinal),
            countryNames);
        Assert.Equal(countryNames.Count, countryNames.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(countryNames, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void GetCountryNamesDoesNotExposeMutableCollection()
    {
        var service = CreateService();
        var countryNames = service.GetCountryNames();
        var mutableView = Assert.IsAssignableFrom<IList<string>>(countryNames);

        Assert.Throws<NotSupportedException>(() => mutableView.Add("Spain"));
        Assert.Equal(ExpectedCountryNames, service.GetCountryNames());
    }

    [Fact]
    public void AddDemoCountriesRegistersCountryServiceAsSingleton()
    {
        var services = new ServiceCollection();

        var returnedServices = services.AddDemoCountries();

        Assert.Same(services, returnedServices);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ICountryService>();
        var second = provider.GetRequiredService<ICountryService>();

        Assert.Same(first, second);
    }

    private static ICountryService CreateService()
    {
        var services = new ServiceCollection();
        services.AddDemoCountries();

        return services.BuildServiceProvider().GetRequiredService<ICountryService>();
    }
}
