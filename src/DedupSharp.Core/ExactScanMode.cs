namespace DedupSharp.Core;

/// <summary>
/// Exact duplicate detection strategy.
/// </summary>
public enum ExactScanMode
{
    /// <summary>
    /// For size-groups of 2, use a fast binary comparison.
    /// For groups of 3+, use hashing to group duplicates.
    /// </summary>
    BinaryForPairs_HashForGroups = 0,

    /// <summary>
    /// Use hashing for all candidate groups (no binary compare).
    /// </summary>
    HashOnly = 1,

    /// <summary>
    /// Group by hash, then confirm each candidate with a binary comparison
    /// against the canonical file in the hash group.
    /// </summary>
    HashWithBinaryVerification = 2
}
