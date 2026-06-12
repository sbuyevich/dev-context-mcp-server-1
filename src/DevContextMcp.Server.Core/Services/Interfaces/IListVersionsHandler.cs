using DevContextMcp.Server.Core.Contracts.ListVersions;

namespace DevContextMcp.Server.Core.Services;

public interface IListVersionsHandler
{
    Task<ListVersionsResponse> HandleAsync(ListVersionsRequest request, CancellationToken cancellationToken);
}
