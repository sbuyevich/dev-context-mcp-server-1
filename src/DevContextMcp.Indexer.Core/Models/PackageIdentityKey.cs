using System.Security.Cryptography;
using System.Text;

namespace DevContextMcp.Indexer.Core.Models;

public readonly record struct PackageIdentityKey(string PackageId, string Version)
{
    public string NormalizedPackageId => PackageId.Trim().ToLowerInvariant();

    public string NormalizedVersion => Version.Trim().ToLowerInvariant();

    public string ToStableId(string sourceId)
    {
        var value = $"{sourceId}\n{NormalizedPackageId}\n{NormalizedVersion}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    public override string ToString() => $"{NormalizedPackageId}/{NormalizedVersion}";
}
