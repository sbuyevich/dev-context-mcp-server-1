namespace Demo.Countries;

/// <summary>
/// Provides country names.
/// </summary>
public interface ICountryService
{
    /// <summary>
    /// Gets the available country names in alphabetical order.
    /// </summary>
    /// <returns>A read-only list of country names.</returns>
    IReadOnlyList<string> GetCountryNames();
}
