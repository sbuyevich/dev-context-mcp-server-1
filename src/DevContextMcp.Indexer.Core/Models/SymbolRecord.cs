namespace DevContextMcp.Indexer.Core.Models;

public sealed record SymbolRecord(
    string Namespace,
    string FullyQualifiedName,
    string Kind,
    string Signature,
    string? ContainingType,
    string AssemblyPath,
    string? TargetFramework,
    string? XmlDocumentationMember);
