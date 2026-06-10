namespace DevContextMcp.Infrastructure.Indexer.NuGet;

internal sealed class LengthLimitedStream(Stream inner, long maximumLength) : Stream
{
    private long _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(count);
        inner.Write(buffer, offset, count);
        _written += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(buffer.Length);
        inner.Write(buffer);
        _written += buffer.Length;
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacity(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken);
        _written += buffer.Length;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        EnsureCapacity(count);
        _written += count;
        return WriteCoreAsync(buffer, offset, count, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task WriteCoreAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        try
        {
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }
        catch
        {
            _written -= count;
            throw;
        }
    }

    private void EnsureCapacity(int count)
    {
        if (count < 0 || _written > maximumLength - count)
        {
            throw new InvalidDataException(
                $"Package archive exceeds the configured limit of {maximumLength} bytes.");
        }
    }
}
