using DevContextMcp.Server.Core.Models;
using DevContextMcp.Server.Core.Contracts.Common;
using NuGet.Versioning;

namespace DevContextMcp.Server.Core.Services;

internal static class RetrievalHandlerSupport
{
    public static CancellationTokenSource CreateTimeout(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    public static bool IsInvalidVersion(string? value) =>
        value is not null && !NuGetVersion.TryParse(value, out _);

    public static ToolError Error(string code, string message) =>
        new() { Code = code, Message = message };

    public static ToolWarning Warning(string code, string message) =>
        new() { Code = code, Message = message };

    public static ToolError IndexUnavailable(IndexUnavailableException exception) =>
        Error("index_unavailable", exception.Message);

    public static IReadOnlyList<string> SymbolTerms(string question)
    {
        var tokens = question
            .Split(
                [' ', '\t', '\r', '\n', '.', ',', '(', ')', '[', ']', '{', '}', ':', ';', '?', '!'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(token => token.Length)
            .Take(8)
            .ToArray();

        return tokens.Length == 0 ? [question] : tokens;
    }
}
