namespace DevContextMcp.Server.Core.Models;

public sealed record VersionResolution(
    IndexedVersionRecord Version,
    string Reason,
    IReadOnlyList<string> WarningCodes);
