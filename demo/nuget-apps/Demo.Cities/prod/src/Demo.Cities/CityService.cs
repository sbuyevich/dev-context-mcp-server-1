using System.Collections.ObjectModel;

namespace Demo.Cities;

internal sealed class CityService : ICityService
{
    private static readonly ReadOnlyCollection<string> CityNames = Array.AsReadOnly(
    [
        "Berlin",
        "Chicago",
        "London",
        "Paris",
        "Tokyo",
        "Toronto"
    ]);

    public IReadOnlyList<string> GetCityNames() => CityNames;
}
