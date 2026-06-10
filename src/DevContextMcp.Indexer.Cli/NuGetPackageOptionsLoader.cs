using System.Text.Json;
using System.Text.Json.Serialization;
using DevContextMcp.Indexer.Cli.Configuration;

namespace DevContextMcp.Indexer.Cli;

internal interface INuGetPackageOptionsLoader
{
    IReadOnlyList<NuGetPackageOptions> Load(string configuredPath);

    string ResolvePath(string configuredPath);
}

internal sealed class NuGetPackageOptionsLoader : INuGetPackageOptionsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly object _gate = new();
    private string? _loadedPath;
    private IReadOnlyList<NuGetPackageOptions>? _packages;

    public IReadOnlyList<NuGetPackageOptions> Load(string configuredPath)
    {
        var path = ResolvePath(configuredPath);

        lock (_gate)
        {
            if (_packages is not null)
            {
                if (!string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"NuGet package configuration was already loaded from '{_loadedPath}', not '{path}'.");
                }

                return _packages;
            }

            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException(
                    $"NuGet package configuration folder '{path}' does not exist.");
            }

            var packages = new List<NuGetPackageOptions>();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                throw new InvalidOperationException(
                    $"NuGet package configuration folder '{path}' could not be read.",
                    exception);
            }

            foreach (var file in files)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    packages.Add(
                        JsonSerializer.Deserialize<NuGetPackageOptions>(
                            stream,
                            SerializerOptions)
                        ?? throw new JsonException("The JSON document was empty."));
                }
                catch (Exception exception) when (
                    exception is JsonException
                    or IOException
                    or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        $"NuGet package configuration file '{file}' could not be loaded.",
                        exception);
                }
            }

            _loadedPath = path;
            _packages = packages;
            return _packages;
        }
    }

    public string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath));
    }
}
