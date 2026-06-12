using System.Text;
using System.Xml.Linq;
using DevContextMcp.Indexer.Core.Infrastructure;
using DevContextMcp.Indexer.Core.Models;

namespace DevContextMcp.Infrastructure.Indexer.Processing;

internal sealed class DocumentChunker(IContentHasher hasher) : IDocumentChunker
{
    public IReadOnlyList<DocumentChunkRecord> Chunk(
        string path,
        string kind,
        string content,
        int maxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        return kind.Equals("xml_documentation", StringComparison.Ordinal)
            ? ChunkXml(path, content, maxCharacters)
            : ChunkText(path, kind, content, maxCharacters);
    }

    private IReadOnlyList<DocumentChunkRecord> ChunkXml(
        string path,
        string content,
        int maxCharacters)
    {
        try
        {
            var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
            var members = document.Root?
                .Element("members")?
                .Elements("member")
                .ToArray() ?? [];

            if (members.Length == 0)
            {
                return ChunkText(path, "xml_documentation", content, maxCharacters);
            }

            var chunks = new List<DocumentChunkRecord>();
            foreach (var member in members)
            {
                var memberName = member.Attribute("name")?.Value;
                var text = NormalizeWhitespace(string.Join(
                    Environment.NewLine,
                    member.Elements().Select(element =>
                        $"{element.Name.LocalName}: {NormalizeWhitespace(element.Value)}")));

                AddBoundedChunks(
                    chunks,
                    path,
                    "xml_documentation",
                    memberName,
                    text,
                    maxCharacters);
            }

            return chunks;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.Xml.XmlException)
        {
            return ChunkText(path, "xml_documentation", content, maxCharacters);
        }
    }

    private IReadOnlyList<DocumentChunkRecord> ChunkText(
        string path,
        string kind,
        string content,
        int maxCharacters)
    {
        var chunks = new List<DocumentChunkRecord>();
        var sections = SplitSections(content);

        foreach (var section in sections)
        {
            AddBoundedChunks(chunks, path, kind, null, section, maxCharacters);
        }

        return chunks;
    }

    private void AddBoundedChunks(
        List<DocumentChunkRecord> chunks,
        string path,
        string kind,
        string? memberName,
        string content,
        int maxCharacters)
    {
        var remaining = content.Trim();
        while (remaining.Length > 0)
        {
            var length = Math.Min(maxCharacters, remaining.Length);
            if (length < remaining.Length)
            {
                var boundary = remaining.LastIndexOfAny(
                    ['\n', '.', ' ', ';', ','],
                    length - 1,
                    length);
                if (boundary >= maxCharacters / 2)
                {
                    length = boundary + 1;
                }
            }

            var chunk = remaining[..length].Trim();
            remaining = remaining[length..].TrimStart();
            if (chunk.Length == 0)
            {
                continue;
            }

            chunks.Add(new(
                path,
                kind,
                memberName,
                chunks.Count,
                chunk,
                hasher.Hash(Encoding.UTF8.GetBytes(chunk))));
        }
    }

    private static IReadOnlyList<string> SplitSections(string content)
    {
        var normalized = content.ReplaceLineEndings("\n");
        var sections = normalized.Split(
            ["\n\n", "\n#"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return sections.Length == 0 ? [normalized] : sections;
    }

    private static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
            }
            else
            {
                builder.Append(character);
                previousWasWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }
}
