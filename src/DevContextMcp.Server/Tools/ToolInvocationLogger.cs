using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DevContextMcp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DevContextMcp.Server.Tools;

internal sealed class ToolInvocationLogger(
    IOptions<DevContextMcpOptions> options,
    ILogger<ToolInvocationLogger> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string toolName,
        TRequest request,
        Func<CancellationToken, Task<TResponse>> invoke,
        CancellationToken cancellationToken)
    {
        var invocationId = Guid.NewGuid().ToString("N");
        var debugEnabled = IsDebugEnabled();
        if (debugEnabled)
        {
            LogPayload("request", toolName, invocationId, request, null);
        }

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var response = await invoke(cancellationToken);
            if (debugEnabled)
            {
                LogPayload(
                    "response",
                    toolName,
                    invocationId,
                    response,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            SafeLog(
                LogLevel.Debug,
                null,
                "MCP tool {ToolName} invocation {InvocationId} was canceled after {ElapsedMilliseconds} ms.",
                toolName,
                invocationId,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            SafeLog(
                LogLevel.Error,
                exception,
                "MCP tool {ToolName} invocation {InvocationId} failed after {ElapsedMilliseconds} ms.",
                toolName,
                invocationId,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }

    private bool IsDebugEnabled()
    {
        try
        {
            return logger.IsEnabled(LogLevel.Debug);
        }
        catch
        {
            return false;
        }
    }

    private void LogPayload<TPayload>(
        string direction,
        string toolName,
        string invocationId,
        TPayload payload,
        double? elapsedMilliseconds)
    {
        try
        {
            var serialized = Serialize(payload, options.Value.ToolLogging.MaxPayloadBytes);
            if (elapsedMilliseconds is null)
            {
                SafeLog(
                    LogLevel.Debug,
                    null,
                    "MCP tool {ToolName} invocation {InvocationId} {Direction}. PayloadBytes={PayloadBytes} PayloadTruncated={PayloadTruncated} Payload={Payload}",
                    toolName,
                    invocationId,
                    direction,
                    serialized.OriginalUtf8Bytes,
                    serialized.Truncated,
                    serialized.Json);
            }
            else
            {
                SafeLog(
                    LogLevel.Debug,
                    null,
                    "MCP tool {ToolName} invocation {InvocationId} {Direction} after {ElapsedMilliseconds} ms. PayloadBytes={PayloadBytes} PayloadTruncated={PayloadTruncated} Payload={Payload}",
                    toolName,
                    invocationId,
                    direction,
                    elapsedMilliseconds.Value,
                    serialized.OriginalUtf8Bytes,
                    serialized.Truncated,
                    serialized.Json);
            }
        }
        catch
        {
            // Diagnostic logging must never change tool behavior.
        }
    }

    private void SafeLog(
        LogLevel level,
        Exception? exception,
        string message,
        params object?[] values)
    {
        try
        {
            logger.Log(level, exception, message, values);
        }
        catch
        {
            // Diagnostic logging must never change tool behavior.
        }
    }

    private static SerializedPayload Serialize<TPayload>(
        TPayload payload,
        int maxPayloadBytes)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        if (bytes.Length <= maxPayloadBytes)
        {
            return new(Encoding.UTF8.GetString(bytes), bytes.Length, false);
        }

        var json = Encoding.UTF8.GetString(bytes);
        var previewLength = FindLargestPreviewLength(
            json,
            bytes.Length,
            maxPayloadBytes);
        var envelope = new TruncatedPayloadEnvelope(
            json[..previewLength],
            true,
            bytes.Length);
        return new(
            JsonSerializer.Serialize(envelope, SerializerOptions),
            bytes.Length,
            true);
    }

    private static int FindLargestPreviewLength(
        string json,
        int originalUtf8Bytes,
        int maxPayloadBytes)
    {
        var low = 0;
        var high = json.Length;
        while (low < high)
        {
            var candidate = low + ((high - low + 1) / 2);
            var previewLength = AvoidSplitSurrogate(json, candidate);
            var envelope = new TruncatedPayloadEnvelope(
                json[..previewLength],
                true,
                originalUtf8Bytes);
            var length = JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                SerializerOptions).Length;
            if (length <= maxPayloadBytes)
            {
                low = candidate;
            }
            else
            {
                high = Math.Max(0, candidate - 1);
            }
        }

        return AvoidSplitSurrogate(json, low);
    }

    private static int AvoidSplitSurrogate(string value, int length) =>
        length > 0
        && length < value.Length
        && char.IsHighSurrogate(value[length - 1])
            ? length - 1
            : length;

    private sealed record TruncatedPayloadEnvelope(
        string Preview,
        bool Truncated,
        int OriginalUtf8Bytes);

    private sealed record SerializedPayload(
        string Json,
        int OriginalUtf8Bytes,
        bool Truncated);
}
