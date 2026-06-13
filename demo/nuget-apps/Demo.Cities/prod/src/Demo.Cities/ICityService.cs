namespace Demo.Cities;

/// <summary>
/// Provides city names.
/// </summary>
public interface ICityService
{
    /// <summary>
    /// Gets the available city names in alphabetical order.
    /// </summary>
    /// <returns>A read-only list of city names.</returns>
    IReadOnlyList<string> GetCityNames();
}
