using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Services;

public interface IVersionResolver
{
    VersionResolution? Resolve(
        IReadOnlyList<IndexedVersionRecord> versions,
        string? requestedVersion,
        string? projectVersion,
        string? recommendedVersion,
        bool includePrerelease);
}
