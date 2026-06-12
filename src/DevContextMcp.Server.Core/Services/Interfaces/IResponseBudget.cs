namespace DevContextMcp.Server.Core.Services;

public interface IResponseBudget
{
    IReadOnlyList<T> Take<T>(
        IReadOnlyList<T> values,
        int maximumCount,
        int maximumBytes,
        Func<T, string> textSelector,
        out bool truncated);
}
