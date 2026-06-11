namespace DevContextMcp.Server.Core.Models;

public sealed record LibraryResolutionResult(
    LibraryResolutionStatus Status,
    ResolvedLibrarySelection? Selection = null);
