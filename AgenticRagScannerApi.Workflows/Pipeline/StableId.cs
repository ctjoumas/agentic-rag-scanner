using System.Security.Cryptography;
using System.Text;

namespace AgenticRagScannerApi.Workflows.Pipeline;

/// <summary>
/// Deterministic, stable identifier derivation for idempotent upsert. The id is a hash of the
/// primary source URL so the same source yields the same id across passes and runs (the quality
/// gate / Cosmos store relies on this in Epic 8).
/// </summary>
public static class StableId
{
    /// <summary>Returns a stable 16-char hex id derived from the normalized URL.</summary>
    public static string FromUrl(string url)
    {
        var normalized = url.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
