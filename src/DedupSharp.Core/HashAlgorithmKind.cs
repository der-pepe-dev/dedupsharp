using System.Text.Json.Serialization;

namespace DedupSharp.Core;

/// <summary>
/// Hash algorithm used to bucket duplicate candidates.
/// </summary>
/// <remarks>
/// Cryptographic hashes (SHA-256) are trusted for grouping. Non-cryptographic
/// hashes (XxHash*) are fast but can collide, so they are only used to bucket
/// candidates; buckets are then split by binary content to guarantee correctness.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HashAlgorithmKind
{
    /// <summary>SHA-256 (cryptographic, trusted for grouping).</summary>
    Sha256 = 0,

    /// <summary>XxHash3 64-bit (non-cryptographic; bucket + binary verify).</summary>
    XxHash3 = 1,

    /// <summary>XxHash128 (non-cryptographic; bucket + binary verify).</summary>
    XxHash128 = 2
}
