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
    /// File size shared by all files in the group (bytes).
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// Files in this group.
    /// </summary>
    public IReadOnlyList<FileEntry> Files { get; }

    /// <summary>
    /// Creates a duplicate group with the given size and files.
    /// </summary>
    public DuplicateGroup(long sizeBytes, IReadOnlyList<FileEntry> files)
    {
        if (files is null) throw new ArgumentNullException(nameof(files));

        SizeBytes = sizeBytes;
        Files = files.ToList().AsReadOnly();
    }
}
