namespace DevContextMcp.Infrastructure.Indexer.Abstractions;

internal interface IContentHasher
{
    string Hash(ReadOnlySpan<byte> content);

    Task<string> HashAsync(Stream stream, CancellationToken cancellationToken);
}
