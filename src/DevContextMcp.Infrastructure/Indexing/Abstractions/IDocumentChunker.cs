using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Infrastructure.Indexing.Abstractions;

internal interface IDocumentChunker
{
    IReadOnlyList<DocumentChunkRecord> Chunk(
        string path,
        string kind,
        string content,
        int maxCharacters);
}
