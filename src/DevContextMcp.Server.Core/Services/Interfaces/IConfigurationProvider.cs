
using DevContextMcp.Server.Core.Models;

namespace DevContextMcp.Server.Core.Services;

public interface IConfigurationProvider
{
    RetrievalSettings GetSettings();
}
