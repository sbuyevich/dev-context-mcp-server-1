using System.Collections.ObjectModel;

namespace Demo.Cities;

/// <summary>
/// Provides the collection of city names available to the application.
/// </summary>
internal sealed class CityService : ICityService
{
    private static readonly ReadOnlyCollection<string> CityNames = Array.AsReadOnly(
    [
        "berlin",
        "london",
        "paris",
        "tokyo",
        "toronto"
    ]);

    /// <summary>
    /// Gets the supported city names in display order.
    /// </summary>
    /// <returns>A read-only list of city names.</returns>
    public IReadOnlyList<string> GetCityNames() => CityNames;
}
