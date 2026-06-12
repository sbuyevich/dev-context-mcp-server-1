using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.ResolveLibrary;
using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Services;

internal sealed class ResolveLibraryHandler(
    IConfigurationProvider configurationProvider,
    INuGetReadStore store,
    IVersionResolver versionResolver) : IResolveLibraryHandler
{
    public async Task<ResolveLibraryResponse> HandleAsync(
        ResolveLibraryRequest request,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(request.Query, nameof(request.Query));
        Guard.Positive(request.Limit, nameof(request.Limit));
        if (request.Environment is not null
            && !LibraryId.IsValidEnvironment(request.Environment))
        {
            return NotFound(
                "invalid_environment",
                "The environment must contain only letters, numbers, '.', '_', or '-'.");
        }

        var settings = configurationProvider.GetSettings();
        using var timeout = RetrievalHandlerSupport.CreateTimeout(
            settings.Limits.QueryTimeout,
            cancellationToken);

        try
        {
            if (request.Environment is not null
                && !await store.EnvironmentExistsAsync(
                    settings.DatabasePath,
                    request.Environment,
                    timeout.Token))
            {
                return NotFound(
                    "environment_not_found",
                    $"Environment '{request.Environment}' is not indexed.");
            }

            var limit = Math.Min(request.Limit, settings.Limits.MaxResults);
            var rawCandidates = await store.SearchLibrariesAsync(
                settings.DatabasePath,
                request.Query,
                Math.Max(limit * 4, 20),
                timeout.Token);
            var matches = new List<RankedLibraryMatch>();

            foreach (var candidate in rawCandidates.Where(candidate =>
                         request.Environment is null
                         || candidate.Environment.Equals(
                             request.Environment,
                             StringComparison.OrdinalIgnoreCase)))
            {
                if (!request.IncludePrerelease
                    && candidate.LatestPrerelease
                    && !candidate.LatestListed)
                {
                    continue;
                }

                var versions = await store.ListVersionsAsync(
                    settings.DatabasePath,
                    candidate.LibraryId,
                    timeout.Token);
                var recommendation = RecommendedVersionSelector.Find(
                    settings.RecommendedVersions,
                    candidate.Environment,
                    candidate.PackageId);
                var resolution = versionResolver.Resolve(
                    versions,
                    null,
                    null,
                    recommendation,
                    request.IncludePrerelease);
                if (resolution is null)
                {
                    continue;
                }

                var environmentIndex = RetrievalLibraryResolver.OrderIndex(
                    settings.EnvironmentOrder,
                    candidate.Environment);
                var sourceIndex = RetrievalLibraryResolver.OrderIndex(
                    settings.SourceOrder,
                    candidate.SourceName);
                var score = candidate.ExactId
                    ? 1.0
                    : candidate.PrefixId ? 0.9 : candidate.TextScore;
                if (recommendation is not null
                    && versions.Any(version =>
                        version.Version.Equals(recommendation, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 0.05;
                }
                var recommendationAvailable = recommendation is not null
                    && versions.Any(version =>
                        version.Version.Equals(
                            recommendation,
                            StringComparison.OrdinalIgnoreCase));

                if (sourceIndex != int.MaxValue)
                {
                    score += Math.Max(0, 0.03 - (sourceIndex * 0.005));
                }

                if (resolution.Version.Deprecated)
                {
                    score -= 0.10;
                }

                matches.Add(new(
                    new LibraryMatch
                    {
                        LibraryId = new LibraryId(
                            candidate.PackageId,
                            candidate.Environment).ToString(),
                        Kind = "nuget",
                        DisplayName = candidate.PackageId,
                        Environment = candidate.Environment,
                        SourceId = candidate.SourceName,
                        RecommendedVersion = resolution.Version.Version,
                        Description = candidate.Description,
                        Confidence = Math.Clamp(score, 0, 1)
                    },
                    candidate.PackageId,
                    candidate.Environment,
                    environmentIndex,
                    sourceIndex,
                    recommendationAvailable));
            }

            var selected = matches
                .GroupBy(match =>
                    $"{match.PackageId.ToUpperInvariant()}\n{match.Environment.ToUpperInvariant()}",
                    StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(item => item.RecommendationAvailable)
                    .ThenBy(item => item.SourceIndex)
                    .ThenByDescending(item => item.Match.Confidence)
                    .ThenBy(item => item.Match.DisplayName, StringComparer.Ordinal)
                    .First())
                .OrderByDescending(item => item.Match.Confidence)
                .ThenBy(item => item.EnvironmentIndex)
                .ThenBy(item => item.Environment, StringComparer.Ordinal)
                .ThenBy(item => item.Match.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(item => item.Match)
                .ToArray();

            return selected.Length == 0
                ? new ResolveLibraryResponse
                {
                    Status = ToolResultStatus.NotFound,
                    Data = new ResolveLibraryResult(),
                    Errors =
                    [
                        RetrievalHandlerSupport.Error(
                            "library_not_found",
                            $"No indexed NuGet package matched '{request.Query}'.")
                    ]
                }
                : new ResolveLibraryResponse
                {
                    Status = ToolResultStatus.Ok,
                    Data = new ResolveLibraryResult { Matches = selected }
                };
        }
        catch (IndexUnavailableException exception)
        {
            return new ResolveLibraryResponse
            {
                Status = ToolResultStatus.NotFound,
                Data = new ResolveLibraryResult(),
                Errors = [RetrievalHandlerSupport.IndexUnavailable(exception)]
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResolveLibraryResponse
            {
                Status = ToolResultStatus.InsufficientEvidence,
                Data = new ResolveLibraryResult(),
                Errors =
                [
                    RetrievalHandlerSupport.Error(
                        "query_timeout",
                        "Library resolution exceeded the configured timeout.")
                ]
            };
        }
    }

    private static ResolveLibraryResponse NotFound(string code, string message) =>
        new()
        {
            Status = ToolResultStatus.NotFound,
            Data = new ResolveLibraryResult(),
            Errors = [RetrievalHandlerSupport.Error(code, message)]
        };

    private sealed record RankedLibraryMatch(
        LibraryMatch Match,
        string PackageId,
        string Environment,
        int EnvironmentIndex,
        int SourceIndex,
        bool RecommendationAvailable);
}
