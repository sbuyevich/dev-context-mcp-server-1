using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.GetSymbol;
using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Services;

internal sealed class GetSymbolHandler(
    RetrievalSettings settings,
    ILibraryResolver libraryResolver,
    INuGetReadStore store,
    ICitationFactory citationFactory) : IGetSymbolHandler
{
    public async Task<GetSymbolResponse> HandleAsync(
        GetSymbolRequest request,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(request.LibraryId, nameof(request.LibraryId));
        Guard.NotBlank(request.Symbol, nameof(request.Symbol));
        if (!LibraryId.TryParse(request.LibraryId, out var libraryId))
        {
            return NotFound(
                "invalid_library_id",
                "The library ID must use the 'nuget:' or 'docs:' prefix.");
        }
        if (libraryId.Kind.Equals("docs", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(
                "symbol_lookup_not_supported",
                "Symbol lookup is not supported for company documentation.");
        }
        if (RetrievalHandlerSupport.IsInvalidVersion(request.Version)
            || RetrievalHandlerSupport.IsInvalidVersion(request.ProjectVersion))
        {
            return NotFound("invalid_version", "The requested package version is not valid.");
        }

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
                request.Version,
                request.ProjectVersion,
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
            var version = selection.Version;
            if (version is null)
            {
                return NotFound(
                    request.Version is not null || request.ProjectVersion is not null
                        ? "version_not_found"
                        : "stable_version_not_found",
                    "No indexed package version matched the request.");
            }

            var hits = await store.SearchSymbolsAsync(
                settings.DatabasePath,
                version.Version.LibraryVersionId,
                request.Symbol,
                request.TargetFramework,
                settings.Limits.AmbiguousSymbolLimit * 4,
                timeout.Token);
            if (hits.Count == 0)
            {
                return new GetSymbolResponse
                {
                    Status = ToolResultStatus.NotFound,
                    Data = new GetSymbolResult(),
                    ResolvedContext = Context(selection, version),
                    Errors =
                    [
                        RetrievalHandlerSupport.Error(
                            "symbol_not_found",
                            $"Symbol '{request.Symbol}' was not found in the selected package version.")
                    ]
                };
            }

            var grouped = hits
                .GroupBy(
                    hit => $"{hit.FullyQualifiedName}\n{hit.Kind}\n{hit.Signature}",
                    StringComparer.Ordinal)
                .Select(group => new SymbolGroup(
                    group.First(),
                    group
                        .Select(item => item.TargetFramework)
                        .Where(item => item is not null)
                        .Select(item => item!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .OrderBy(group => group.Symbol.MatchTier)
                .ThenBy(group => group.Symbol.FullyQualifiedName, StringComparer.Ordinal)
                .ToArray();
            var winningTier = grouped[0].Symbol.MatchTier;
            var winners = grouped
                .Where(group => group.Symbol.MatchTier == winningTier)
                .Take(settings.Limits.AmbiguousSymbolLimit)
                .ToArray();

            if (winners.Length > 1)
            {
                return new GetSymbolResponse
                {
                    Status = ToolResultStatus.InsufficientEvidence,
                    Data = new GetSymbolResult
                    {
                        Candidates = winners
                            .Select(group => ToDetails(selection, version, group, []))
                            .ToArray()
                    },
                    ResolvedContext = Context(selection, version),
                    Warnings =
                    [
                        RetrievalHandlerSupport.Warning(
                            "ambiguous_symbol",
                            "Multiple symbols matched at the same confidence tier.")
                    ]
                };
            }

            var winner = winners[0];
            var related = winner.Symbol.ContainingType is null
                ? []
                : await store.GetRelatedSymbolsAsync(
                    settings.DatabasePath,
                    version.Version.LibraryVersionId,
                    winner.Symbol.ContainingType,
                    winner.Symbol.FullyQualifiedName,
                    10,
                    timeout.Token);
            var details = ToDetails(selection, version, winner, related);
            var warnings = version.WarningCodes
                .Select(code => RetrievalHandlerSupport.Warning(
                    code,
                    "The configured recommended version is not indexed; a fallback version was selected."))
                .ToList();
            if (version.Version.Deprecated)
            {
                warnings.Add(RetrievalHandlerSupport.Warning(
                    "deprecated_version",
                    "The selected package version is deprecated."));
            }

            return new GetSymbolResponse
            {
                Status = ToolResultStatus.Ok,
                Data = new GetSymbolResult { Symbol = details },
                ResolvedContext = Context(selection, version),
                Evidence =
                [
                    new EvidenceItem
                    {
                        Kind = "symbol",
                        Title = details.FullyQualifiedName,
                        Text = details.Documentation is null
                            ? details.Signature
                            : $"{details.Signature}{Environment.NewLine}{details.Documentation}",
                        Score = 1,
                        CitationUri = details.CitationUri
                    }
                ],
                Citations =
                [
                    new Citation
                    {
                        Uri = details.CitationUri!,
                        Label = details.FullyQualifiedName,
                        Location = winner.Symbol.XmlDocumentationMember
                    }
                ],
                Warnings = warnings
            };
        }
        catch (IndexUnavailableException exception)
        {
            return new GetSymbolResponse
            {
                Status = ToolResultStatus.NotFound,
                Data = new GetSymbolResult(),
                Errors = [RetrievalHandlerSupport.IndexUnavailable(exception)]
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GetSymbolResponse
            {
                Status = ToolResultStatus.InsufficientEvidence,
                Data = new GetSymbolResult(),
                Errors =
                [
                    RetrievalHandlerSupport.Error(
                        "query_timeout",
                        "Symbol lookup exceeded the configured timeout.")
                ]
            };
        }
    }

    private SymbolDetails ToDetails(
        ResolvedLibrarySelection selection,
        VersionResolution version,
        SymbolGroup group,
        IReadOnlyList<SymbolHitRecord> related)
    {
        return new SymbolDetails
        {
            FullyQualifiedName = group.Symbol.FullyQualifiedName,
            Kind = group.Symbol.Kind,
            Signature = group.Symbol.Signature,
            Documentation = group.Symbol.Documentation,
            Assembly = group.Symbol.AssemblyPath,
            TargetFrameworks = group.TargetFrameworks,
            CitationUri = citationFactory.SymbolUri(
                selection.Library.SourceName,
                selection.Library.PackageId,
                version.Version.Version,
                group.Symbol.FullyQualifiedName),
            RelatedMembers = related.Select(item => new RelatedSymbol
            {
                FullyQualifiedName = item.FullyQualifiedName,
                Kind = item.Kind,
                Signature = item.Signature
            }).ToArray()
        };
    }

    private static ResolvedContext Context(
        ResolvedLibrarySelection selection,
        VersionResolution version) =>
        new()
        {
            LibraryId = new LibraryId(
                selection.Library.PackageId,
                selection.Library.Environment).ToString(),
            SourceId = selection.Library.SourceName,
            Environment = selection.Library.Environment,
            Version = version.Version.Version,
            VersionSelectionReason = version.Reason
        };

    private static GetSymbolResponse NotFound(string code, string message) =>
        new()
        {
            Status = ToolResultStatus.NotFound,
            Data = new GetSymbolResult(),
            Errors = [RetrievalHandlerSupport.Error(code, message)]
        };

    private sealed record SymbolGroup(
        SymbolHitRecord Symbol,
        IReadOnlyList<string> TargetFrameworks);
}
