using DevContextMcp.Server.Core.Contracts.Common;
using DevContextMcp.Server.Core.Contracts.QueryDocs;
using DevContextMcp.Server.Core.Infrastructure;
using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Services;

internal sealed class QueryDocsHandler(
    IConfigurationProvider configurationProvider,
    ILibraryResolver libraryResolver,
    INuGetReadStore store,
    ICitationFactory citationFactory,
    IResponseBudget responseBudget) : IQueryDocsHandler
{
    public async Task<QueryDocsResponse> HandleAsync(
        QueryDocsRequest request,
        CancellationToken cancellationToken)
    {
        Guard.NotBlank(request.LibraryId, nameof(request.LibraryId));
        Guard.NotBlank(request.Question, nameof(request.Question));
        Guard.Positive(request.MaxResults, nameof(request.MaxResults));

        if (!LibraryId.TryParse(request.LibraryId, out var libraryId))
        {
            return NotFound("invalid_library_id", "The library ID must use the 'nuget:' prefix.");
        }

        if (RetrievalHandlerSupport.IsInvalidVersion(request.Version)
            || RetrievalHandlerSupport.IsInvalidVersion(request.ProjectVersion))
        {
            return NotFound("invalid_version", "The requested package version is not valid.");
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
                var code = request.Version is not null || request.ProjectVersion is not null
                    ? "version_not_found"
                    : "stable_version_not_found";
                return NotFound(code, "No indexed package version matched the request.");
            }

            var maximumResults = Math.Min(request.MaxResults, settings.Limits.MaxResults);
            var documents = await store.SearchDocumentsAsync(
                settings.DatabasePath,
                version.Version.LibraryVersionId,
                request.Question,
                Math.Max(maximumResults * 3, 20),
                timeout.Token);
            var symbols = await SearchSymbolsAsync(
                settings,
                version.Version.LibraryVersionId,
                request.Question,
                request.TargetFramework,
                Math.Max(maximumResults * 2, 20),
                timeout.Token);

            var ranked = new List<RankedEvidence>();
            ranked.AddRange(documents.Select(document =>
            {
                var score = document.Rank
                    + (document.Kind == "readme" ? 0.10
                        : document.Kind == "xml_documentation" ? 0.05 : 0);
                var uri = citationFactory.ArtifactUri(
                    selection.Library.SourceName,
                    selection.Library.PackageId,
                    version.Version.Version,
                    document.Path);
                return new RankedEvidence(
                    document.Kind,
                    document.MemberName ?? document.Path,
                    document.Content,
                    Math.Round(Math.Min(1, score), 6),
                    uri,
                    document.MemberName,
                    null,
                    document.ContentHash);
            }));
            ranked.AddRange(symbols.Select(symbol =>
            {
                var score = symbol.MatchTier switch
                {
                    0 => 1.0,
                    1 => 0.95,
                    2 => 0.90,
                    3 => 0.85,
                    _ => 0.80
                };
                var uri = citationFactory.SymbolUri(
                    selection.Library.SourceName,
                    selection.Library.PackageId,
                    version.Version.Version,
                    symbol.FullyQualifiedName);
                var text = symbol.Documentation is null
                    ? symbol.Signature
                    : $"{symbol.Signature}{Environment.NewLine}{symbol.Documentation}";
                return new RankedEvidence(
                    "symbol",
                    symbol.FullyQualifiedName,
                    text,
                    score,
                    uri,
                    symbol.XmlDocumentationMember,
                    symbol,
                    null);
            }));

            if (version.Version.Deprecated)
            {
                ranked = ranked
                    .Select(item => item with { Score = item.Score * 0.85 })
                    .ToList();
            }

            var ordered = ranked
                .Where(item => item.Score >= settings.Limits.MinimumEvidenceScore)
                .GroupBy(
                    item => item.ContentHash is null
                        ? $"{item.Kind}\n{item.Uri}\n{item.Text}"
                        : $"document\n{item.ContentHash}",
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Kind == "symbol" ? 1 : 0)
                .ThenBy(item => item.Uri, StringComparer.Ordinal)
                .ThenBy(item => item.Text, StringComparer.Ordinal)
                .ToArray();
            var selected = responseBudget.Take(
                ordered,
                maximumResults,
                settings.Limits.MaxResponseBytes,
                item => item.Text,
                out var truncated);

            var warnings = BuildWarnings(version, truncated);
            var context = Context(selection, version);
            if (selected.Count == 0)
            {
                return new QueryDocsResponse
                {
                    Status = ToolResultStatus.InsufficientEvidence,
                    Data = new QueryDocsResult(),
                    ResolvedContext = context,
                    Warnings = warnings,
                    Errors =
                    [
                        RetrievalHandlerSupport.Error(
                            "insufficient_evidence",
                            "The package version is indexed, but no sufficiently relevant evidence was found.")
                    ]
                };
            }

            var citations = selected
                .Select(item => new Citation
                {
                    Uri = item.Uri,
                    Label = item.Title,
                    Location = item.Location
                })
                .GroupBy(citation => citation.Uri, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            return new QueryDocsResponse
            {
                Status = ToolResultStatus.Ok,
                Data = new QueryDocsResult
                {
                    Fragments = selected
                        .Where(item => item.Symbol is null)
                        .Select(item => new DocumentFragment
                        {
                            Title = item.Title,
                            Text = item.Text,
                            CitationUri = item.Uri
                        })
                        .ToArray(),
                    Symbols = selected
                        .Where(item => item.Symbol is not null)
                        .Select(item => new SymbolReference
                        {
                            Name = item.Symbol!.FullyQualifiedName,
                            Signature = item.Symbol.Signature,
                            CitationUri = item.Uri
                        })
                        .ToArray()
                },
                ResolvedContext = context,
                Evidence = selected.Select(item => new EvidenceItem
                {
                    Kind = item.Kind,
                    Title = item.Title,
                    Text = item.Text,
                    Score = item.Score,
                    CitationUri = item.Uri
                }).ToArray(),
                Citations = citations,
                Warnings = warnings
            };
        }
        catch (IndexUnavailableException exception)
        {
            return new QueryDocsResponse
            {
                Status = ToolResultStatus.NotFound,
                Data = new QueryDocsResult(),
                Errors = [RetrievalHandlerSupport.IndexUnavailable(exception)]
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new QueryDocsResponse
            {
                Status = ToolResultStatus.InsufficientEvidence,
                Data = new QueryDocsResult(),
                Errors =
                [
                    RetrievalHandlerSupport.Error(
                        "query_timeout",
                        "Documentation retrieval exceeded the configured timeout.")
                ]
            };
        }
    }

    private async Task<IReadOnlyList<SymbolHitRecord>> SearchSymbolsAsync(
        RetrievalSettings settings,
        string libraryVersionId,
        string question,
        string? targetFramework,
        int limit,
        CancellationToken cancellationToken)
    {
        var values = new List<SymbolHitRecord>();
        foreach (var term in RetrievalHandlerSupport.SymbolTerms(question))
        {
            values.AddRange(await store.SearchSymbolsAsync(
                settings.DatabasePath,
                libraryVersionId,
                term,
                targetFramework,
                limit,
                cancellationToken));
        }

        return values
            .GroupBy(
                symbol => $"{symbol.FullyQualifiedName}\n{symbol.Signature}",
                StringComparer.Ordinal)
            .Select(group => group.OrderBy(symbol => symbol.MatchTier).First())
            .OrderBy(symbol => symbol.MatchTier)
            .ThenBy(symbol => symbol.FullyQualifiedName, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static IReadOnlyList<ToolWarning> BuildWarnings(
        VersionResolution version,
        bool truncated)
    {
        var warnings = version.WarningCodes.Select(code =>
            RetrievalHandlerSupport.Warning(
                code,
                "The configured recommended version is not indexed; a fallback version was selected."))
            .ToList();
        if (version.Version.Deprecated)
        {
            warnings.Add(RetrievalHandlerSupport.Warning(
                "deprecated_version",
                "The selected package version is deprecated."));
        }

        if (truncated)
        {
            warnings.Add(RetrievalHandlerSupport.Warning(
                "response_truncated",
                "Some evidence was omitted to respect the configured response limit."));
        }

        return warnings;
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

    private static QueryDocsResponse NotFound(string code, string message) =>
        new()
        {
            Status = ToolResultStatus.NotFound,
            Data = new QueryDocsResult(),
            Errors = [RetrievalHandlerSupport.Error(code, message)]
        };

    private sealed record RankedEvidence(
        string Kind,
        string Title,
        string Text,
        double Score,
        string Uri,
        string? Location,
        SymbolHitRecord? Symbol,
        string? ContentHash);
}
