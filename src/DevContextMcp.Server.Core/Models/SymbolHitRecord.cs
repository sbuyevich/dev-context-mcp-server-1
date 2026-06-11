namespace DevContextMcp.Server.Core.Models;

public sealed record SymbolHitRecord(
    string FullyQualifiedName,
    string Kind,
    string Signature,
    string? ContainingType,
    string AssemblyPath,
    string? TargetFramework,
    string? XmlDocumentationMember,
    string? Documentation,
    int MatchTier);
