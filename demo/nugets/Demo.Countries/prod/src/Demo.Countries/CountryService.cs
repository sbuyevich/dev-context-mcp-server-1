using System.Collections.ObjectModel;

namespace Demo.Countries;

internal sealed class CountryService : ICountryService
{
    private static readonly ReadOnlyCollection<string> CountryNames = Array.AsReadOnly(
    [
        "Canada",
        "France",
        "Germany",
        "Japan",
        "United Kingdom",
        "United States"
    ]);

    public IReadOnlyList<string> GetCountryNames() => CountryNames;
}
