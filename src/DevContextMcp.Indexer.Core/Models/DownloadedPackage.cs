namespace DevContextMcp.Indexer.Core.Models;

public sealed class DownloadedPackage(
    string filePath,
    string contentHash,
    long length) : IAsyncDisposable
{
    public string FilePath { get; } = filePath;

    public string ContentHash { get; } = contentHash;

    public long Length { get; } = length;

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (IOException)
        {
            // Cleanup is best-effort; indexing results are already independent of this file.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best-effort; indexing results are already independent of this file.
        }

        return ValueTask.CompletedTask;
    }
}
