namespace DevContextMcp.Server.Core.Models;

public sealed record DocumentHitRecord(
    string Path,
    string Kind,
    string? MemberName,
    string Content,
    string ContentHash,
    double Rank);
