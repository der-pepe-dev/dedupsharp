using System;
using System.Collections.Generic;
using System.Linq;

namespace DedupSharp.Core;

/// <summary>
/// A group of files that are potential duplicates (same size, and later same hash/content).
/// </summary>
public sealed class DuplicateGroup
{
    /// <summary>
    /// What kind of duplicate this group represents (exact bytes, perceptual image, ...).
    /// </summary>
    public DuplicateKind Kind { get; }

    /// <summary>
    /// File size shared by all files in the group (bytes). For non-exact groups this is
    /// the representative file's size and is diagnostic only.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// Files in this group.
    /// </summary>
    public IReadOnlyList<FileEntry> Files { get; }

    /// <summary>
    /// Creates a duplicate group with the given size and files.
    /// </summary>
    public DuplicateGroup(long sizeBytes, IReadOnlyList<FileEntry> files, DuplicateKind kind = DuplicateKind.Exact)
    {
        if (files is null) throw new ArgumentNullException(nameof(files));

        Kind = kind;
        SizeBytes = sizeBytes;
        Files = files.ToList().AsReadOnly();
    }
}
