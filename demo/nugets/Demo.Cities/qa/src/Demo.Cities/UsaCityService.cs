using System.Collections.ObjectModel;

namespace Demo.Cities;

internal sealed class UsaCityService : IUsaCityService
{
    private static readonly ReadOnlyCollection<string> CityNames = Array.AsReadOnly(
    [
        "chicago",
        "houston",
        "los angeles",
        "new york",
        "philadelphia",
        "phoenix"
    ]);

    public IReadOnlyList<string> GetCityNames() => CityNames;
}
