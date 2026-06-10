using System.Text.RegularExpressions;

namespace DevContextMcp.Infrastructure.Server;

internal static partial class FtsQueryBuilder
{
    public static string Build(string value)
    {
        var tokens = TokenPattern()
            .Matches(value)
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .Take(16)
            .Select(token =>
            {
                var escaped = token.Replace("\"", "\"\"", StringComparison.Ordinal);
                return token.Length >= 2 ? $"\"{escaped}\"*" : $"\"{escaped}\"";
            })
            .ToArray();

        return string.Join(" AND ", tokens);
    }

    [GeneratedRegex(@"[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
