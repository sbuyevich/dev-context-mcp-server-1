using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Infrastructure.Indexer.Abstractions;

internal interface IDocumentChunker
{
    IReadOnlyList<DocumentChunkRecord> Chunk(
        string path,
        string kind,
        string content,
        int maxCharacters);
}
