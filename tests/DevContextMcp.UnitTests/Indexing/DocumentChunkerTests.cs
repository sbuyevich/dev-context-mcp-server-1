using DevContextMcp.Infrastructure.Indexer.Processing;

namespace DevContextMcp.UnitTests.Indexing;

public sealed class DocumentChunkerTests
{
    private readonly DocumentChunker _chunker = new(new Sha256ContentHasher());

    [Fact]
    public void XmlDocumentationIsChunkedPerMember()
    {
        const string xml =
            """
            <doc>
              <members>
                <member name="T:Fixture.Widget"><summary>A fixture widget.</summary></member>
                <member name="M:Fixture.Widget.Run"><summary>Runs the widget.</summary></member>
              </members>
            </doc>
            """;

        var chunks = _chunker.Chunk("lib/net10.0/Fixture.xml", "xml_documentation", xml, 4000);

        Assert.Equal(2, chunks.Count);
        Assert.Contains(chunks, chunk => chunk.MemberName == "T:Fixture.Widget");
        Assert.Contains(chunks, chunk => chunk.Content.Contains("fixture widget", StringComparison.Ordinal));
    }

    [Fact]
    public void TextChunksRespectMaximumLength()
    {
        var chunks = _chunker.Chunk(
            "README.md",
            "readme",
            string.Join(' ', Enumerable.Repeat("documentation", 100)),
            100);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Content.Length <= 100));
    }
}
