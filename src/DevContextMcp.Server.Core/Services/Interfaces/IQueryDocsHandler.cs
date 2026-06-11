using DevContextMcp.Server.Core.Contracts.QueryDocs;

namespace DevContextMcp.Server.Core.Services;

public interface IQueryDocsHandler
{
    Task<QueryDocsResponse> HandleAsync(QueryDocsRequest request, CancellationToken cancellationToken);
}
