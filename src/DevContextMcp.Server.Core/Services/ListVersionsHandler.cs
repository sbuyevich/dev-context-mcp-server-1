using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.ListVersions;
using DevContextMcp.Server.Core.Models;
using NuGet.Versioning;

namespace DevContextMcp.Server.Core.Services;

internal sealed class ListVersionsHandler(
    IConfigurationProvider configurationProvider,
    ILibraryResolver libraryResolver) : IListVersionsHandler
{
    public async Task<ListVersionsResponse> HandleAsync(
        ListVersionsRequest request,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(request.LibraryId, nameof(request.LibraryId));
        if (!LibraryId.TryParse(request.LibraryId, out var libraryId))
        {
            return NotFound("invalid_library_id", "The library ID must use the 'nuget:' prefix.");
        }

        var settings = configurationProvider.GetSettings();
        using var timeout = RetrievalHandlerSupport.CreateTimeout(
            settings.Limits.QueryTimeout,
            cancellationToken);

        try
        {
            var resolution = await libraryResolver.ResolveAsync(
                settings.DatabasePath,
                libraryId,
                settings.EnvironmentOrder,
                settings.SourceOrder,
                settings.RecommendedVersions,
                null,
                null,
                request.IncludePrerelease,
                timeout.Token);
            if (resolution.Status == LibraryResolutionStatus.EnvironmentNotFound)
            {
                return NotFound(
                    "environment_not_found",
                    $"Environment '{libraryId.Environment}' is not indexed.");
            }

            if (resolution.Status == LibraryResolutionStatus.LibraryNotFound)
            {
                return NotFound(
                    "library_not_found",
                    $"Library '{request.LibraryId}' is not indexed.");
            }

            var selection = resolution.Selection!;
            var versions = selection.Versions
                .Where(version => request.IncludePrerelease || !version.Prerelease)
                .Select(version => (Record: version, Parsed: Parse(version.Version)))
                .Where(item => item.Parsed is not null)
                .OrderByDescending(item => item.Parsed!, VersionComparer.VersionRelease)
                .Select(item => new LibraryVersion
                {
                    Version = item.Record.Version,
                    Listed = item.Record.Listed,
                    Prerelease = item.Record.Prerelease,
                    Deprecated = item.Record.Deprecated,
                    Indexed = true,
                    PublishedAt = item.Record.PublishedAt
                })
                .ToArray();

            var recommended = selection.Version;
            var warnings = recommended?.WarningCodes
                .Select(code => RetrievalHandlerSupport.Warning(
                    code,
                    "The configured recommended version is not indexed; a fallback version was selected."))
                .ToList() ?? [];
            if (versions.Length == 0)
            {
                warnings.Add(RetrievalHandlerSupport.Warning(
                    "no_matching_versions",
                    "The library is indexed, but no versions match the prerelease filter."));
            }

            return new ListVersionsResponse
            {
                Status = ToolResultStatus.Ok,
                Data = new ListVersionsResult
                {
                    Versions = versions,
                    RecommendedVersion = recommended?.Version.Version,
                    RecommendedVersionReason = recommended?.Reason
                },
                ResolvedContext = new ResolvedContext
                {
                    LibraryId = new LibraryId(
                        selection.Library.PackageId,
                        selection.Library.Environment).ToString(),
                    SourceId = selection.Library.SourceName,
                    Environment = selection.Library.Environment
                },
                Warnings = warnings
            };
        }
        catch (IndexUnavailableException exception)
        {
            return new ListVersionsResponse
            {
                Status = ToolResultStatus.NotFound,
                Data = new ListVersionsResult(),
                Errors = [RetrievalHandlerSupport.IndexUnavailable(exception)]
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ListVersionsResponse
            {
                Status = ToolResultStatus.InsufficientEvidence,
                Data = new ListVersionsResult(),
                Errors =
                [
                    RetrievalHandlerSupport.Error(
                        "query_timeout",
                        "Version listing exceeded the configured timeout.")
                ]
            };
        }
    }

    private static ListVersionsResponse NotFound(string code, string message) =>
        new()
        {
            Status = ToolResultStatus.NotFound,
            Data = new ListVersionsResult(),
            Errors = [RetrievalHandlerSupport.Error(code, message)]
        };

    private static NuGetVersion? Parse(string value) =>
        NuGetVersion.TryParse(value, out var parsed) ? parsed : null;
}
