using System.ComponentModel;
using System.Text.Json;
using DevContextMcp.Server.Core.Services;
using DevContextMcp.Server.Core.Contracts.ResolveLibrary;
using ModelContextProtocol.Server;

namespace DevContextMcp.Server.Tools;

[McpServerToolType]
public sealed class ResolveLibraryTool(IResolveLibraryHandler handler)
{
    [McpServerTool(
        Name = "resolve_library",
        UseStructuredContent = true,
        OutputSchemaType = typeof(ResolveLibraryResponse))]
    [Description("Finds indexed NuGet packages by name or concept, optionally within one environment.")]
    public Task<ResolveLibraryResponse> ResolveLibraryAsync(
        [Description("Package name, client name, or implementation concept to resolve.")] string query,
        [Description("Whether prerelease package versions may be considered.")] bool includePrerelease = false,
        [Description("Maximum number of library matches to return.")] int limit = 10,
        [Description("Optional indexed environment such as qa or production.")] string? environment = null,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(query, includePrerelease, limit, environment);

        return handler.HandleAsync(
            request,
            cancellationToken);
    }

    private static ResolveLibraryRequest CreateRequest(
        string query,
        bool includePrerelease,
        int limit,
        string? environment)
    {
        try
        {
            using var document = JsonDocument.Parse(query);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("query", out var nestedQuery)
                || nestedQuery.ValueKind != JsonValueKind.String)
            {
                return new ResolveLibraryRequest(
                    query,
                    includePrerelease,
                    limit,
                    environment);
            }

            query = nestedQuery.GetString()!;
            if (root.TryGetProperty("includePrerelease", out var nestedPrerelease)
                && nestedPrerelease.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                includePrerelease = nestedPrerelease.GetBoolean();
            }

            if (root.TryGetProperty("limit", out var nestedLimit)
                && nestedLimit.TryGetInt32(out var parsedLimit))
            {
                limit = parsedLimit;
            }

            if (root.TryGetProperty("environment", out var nestedEnvironment)
                && nestedEnvironment.ValueKind == JsonValueKind.String)
            {
                environment = nestedEnvironment.GetString();
            }
        }
        catch (JsonException)
        {
            // A normal search query does not need to be valid JSON.
        }

        return new ResolveLibraryRequest(
            query,
            includePrerelease,
            limit,
            environment);
    }
}
