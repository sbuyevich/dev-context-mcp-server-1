namespace DevContextMcp.Indexer.Configuration;

internal static class ConfigurationValidation
{
    public static bool IsNuGetSource(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value)
            || value.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = Path.GetFullPath(value);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    public static void ValidatePath(
        string value,
        string fieldName,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{fieldName} must not be empty.");
            return;
        }

        try
        {
            _ = Path.GetFullPath(value);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            failures.Add($"{fieldName} is not a valid path: {exception.Message}");
        }
    }
}
