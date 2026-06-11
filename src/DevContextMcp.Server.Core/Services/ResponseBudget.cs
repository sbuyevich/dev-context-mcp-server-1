using System.Text;

namespace DevContextMcp.Server.Core.Services;

internal sealed class ResponseBudget : IResponseBudget
{
    public IReadOnlyList<T> Take<T>(
        IReadOnlyList<T> values,
        int maximumCount,
        int maximumBytes,
        Func<T, string> textSelector,
        out bool truncated)
    {
        var selected = new List<T>(Math.Min(maximumCount, values.Count));
        var bytes = 0;

        foreach (var value in values)
        {
            var size = Encoding.UTF8.GetByteCount(textSelector(value));
            if (selected.Count >= maximumCount || bytes + size > maximumBytes)
            {
                truncated = true;
                return selected;
            }

            selected.Add(value);
            bytes += size;
        }

        truncated = false;
        return selected;
    }
}
