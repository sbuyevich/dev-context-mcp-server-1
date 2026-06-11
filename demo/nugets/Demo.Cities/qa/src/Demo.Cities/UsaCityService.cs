using System.Collections.ObjectModel;

namespace Demo.Cities;

internal sealed class UsaCityService : IUsaCityService
{
    private static readonly ReadOnlyCollection<string> CityNames = Array.AsReadOnly(
    [
        "Chicago",
        "Houston",
        "Los Angeles",
        "New York",
        "Philadelphia",
        "Phoenix"
    ]);

    public IReadOnlyList<string> GetCityNames() => CityNames;
}
