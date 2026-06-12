using DevContextMcp.Server.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DevContextMcp.Server.Core;

/// <summary>
/// Application service registration.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IResolveLibraryHandler, ResolveLibraryHandler>();
        services.AddSingleton<IQueryDocsHandler, QueryDocsHandler>();
        services.AddSingleton<IGetSymbolHandler, GetSymbolHandler>();
        services.AddSingleton<IListVersionsHandler, ListVersionsHandler>();
        services.AddSingleton<IVersionResolver, VersionResolver>();
        services.AddSingleton<ICitationFactory, CitationFactory>();
        services.AddSingleton<IResponseBudget, ResponseBudget>();
        services.AddSingleton<ILibraryResolver, RetrievalLibraryResolver>();
        return services;
    }
}
