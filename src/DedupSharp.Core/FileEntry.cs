using System;

namespace DedupSharp.Core;

/// <summary>
/// Represents a single file participating in duplicate detection.
/// </summary>
public sealed class FileEntry
{
    /// <summary>
    /// Full path to the file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Optional content hash (e.g. SHA-256). May be null if not yet computed.
    /// </summary>
    public byte[]? Hash { get; }

    /// <summary>
    /// Creates a file entry with an optional hash.
    /// </summary>
    public FileEntry(string path, long size, byte[]? hash = null)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Size = size;
        Hash = hash;
    }

    /// <summary>
    /// Returns a new <see cref="FileEntry"/> with the same path/size and the given hash.
    /// </summary>
    public FileEntry WithHash(byte[] hash)
    {
        if (hash is null) throw new ArgumentNullException(nameof(hash));
        return new FileEntry(Path, Size, hash);
    }
}
