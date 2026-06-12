namespace Demo.Cities;

/// <summary>
/// Provides extension methods
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Converts a city name to title case. for example, "new york" becomes "New York".
    /// </summary>
    /// <param name="cityName"></param>
    /// <returns></returns>
    public static string ToCityName(this string cityName)
    {
        return cityName.Split(' ')
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower())
            .Aggregate((current, next) => current + " " + next);
    }
}
