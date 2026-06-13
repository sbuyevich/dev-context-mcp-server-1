namespace Demo.Cities;

/// <summary>
/// Provides United States city names.
/// </summary>
public interface IUsaCityService
{
    /// <summary>
    /// Gets the available United States city names in alphabetical order.
    /// </summary>
    /// <returns>A read-only list of United States city names.</returns>
    IReadOnlyList<string> GetCityNames();
}
