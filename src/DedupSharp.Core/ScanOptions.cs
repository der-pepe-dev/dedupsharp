using System;
using System.Collections.Generic;
using System.Threading;

namespace DedupSharp.Core;

/// <summary>
/// Options controlling how a duplicate scan is performed.
/// </summary>
public sealed class ScanOptions
{
    /// <summary>
    /// Root paths (files or directories) to scan.
    /// </summary>
    public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Whether to recurse into subdirectories.
    /// </summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// When true, perform a first pass that only counts files per size and
    /// only builds detailed entries for sizes that occur &gt; 1 time.
    /// </summary>
    public bool UsePreScan { get; set; } = true;

    /// <summary>
    /// Minimum file size (bytes) to consider for duplicate detection.
    /// </summary>
    public long MinFileSizeBytes { get; set; } = 1;

    /// <summary>
    /// Exact duplicate comparison strategy.
    /// </summary>
    public ExactScanMode ExactMode { get; set; } = ExactScanMode.BinaryForPairs_HashForGroups;

    /// <summary>
    /// Hash algorithm used to bucket candidates. Non-cryptographic algorithms are
    /// faster but are always followed by a binary-content split for correctness.
    /// </summary>
    public HashAlgorithmKind HashAlgorithm { get; set; } = HashAlgorithmKind.Sha256;

    // ----- Media (perceptual image) core options; ignored by the exact engine -----

    /// <summary>
    /// Perceptual hash algorithm used by the media image core.
    /// </summary>
    public PerceptualHashKind PerceptualHash { get; set; } = PerceptualHashKind.DHash;

    /// <summary>
    /// Maximum Hamming distance (0-64) at which two images are considered near-duplicates.
    /// Lower is stricter. Default 10.
    /// </summary>
    public int HammingThreshold { get; set; } = 10;

    /// <summary>
    /// File extensions treated as images by the media core (leading dot, case-insensitive).
    /// </summary>
    public ISet<string> ImageExtensions { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
        };

    /// <summary>
    /// If non-empty, only files whose extension is in this set will be scanned.
    /// Extensions should be stored with the leading dot (e.g. ".txt").
    /// </summary>
    public ISet<string> SafeExtensions { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Directory names to hard-skip (e.g. ".zfs", ".git").
    /// Name comparison is case-insensitive.
    /// </summary>
    public ISet<string> IgnoredDirectoryNames { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// File names to hard-skip (e.g. "thumbs.db").
    /// Name comparison is case-insensitive.
    /// </summary>
    public ISet<string> IgnoredFileNames { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If &gt; 0, how many files between progress callbacks.
    /// If 0 or negative, progress callbacks are disabled.
    /// </summary>
    public int ProgressInterval { get; set; } = 0;

    /// <summary>
    /// Optional progress callback.
    /// </summary>
    public Action<ScanProgress>? Progress { get; set; }

    /// <summary>
    /// Token checked during scan enumeration and hash computation.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    /// <summary>
    /// Reserved for future parallel scanning. Currently unused (scanner is sequential).
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;
}
