using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using Blake3;

namespace DedupSharp.Core.Exact;

using DedupSharp.Core;

/// <summary>
/// Exact duplicate scanner implementation: size grouping + optional pre-scan,
/// hash grouping, and optional binary verification.
/// </summary>
public sealed class ExactDuplicateScanner : IDuplicateScanner
{
    public IEnumerable<DuplicateGroup> Scan(ScanOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.Paths is null || options.Paths.Count == 0)
            throw new ArgumentException("At least one path must be specified.", nameof(options));

        return options.UsePreScan
            ? ScanWithPreScan(options)
            : ScanSinglePass(options);
    }

    // ----------------- Core scan flows -----------------

    private IEnumerable<DuplicateGroup> ScanWithPreScan(ScanOptions options)
    {
        var sizeCounts = new Dictionary<long, int>();
        long preFiles = 0;
        long preBytes = 0;
        int interval = options.ProgressInterval > 0 ? options.ProgressInterval : int.MaxValue;

        foreach (var fi in EnumerateCandidateFiles(options))
        {
            preFiles++;
            preBytes += fi.Length;

            if (sizeCounts.TryGetValue(fi.Length, out var count))
                sizeCounts[fi.Length] = count + 1;
            else
                sizeCounts[fi.Length] = 1;

            if (options.Progress is not null && preFiles % interval == 0)
                options.Progress(new ScanProgress(ScanProgressPhase.PreScan, preFiles, preBytes, false));
        }

        options.Progress?.Invoke(new ScanProgress(ScanProgressPhase.PreScan, preFiles, preBytes, true));

        // Second pass: only build entries for sizes with count > 1
        var sizeGroups = new Dictionary<long, List<FileEntry>>();
        long files = 0;
        long bytes = 0;

        foreach (var fi in EnumerateCandidateFiles(options))
        {
            if (!sizeCounts.TryGetValue(fi.Length, out var count) || count < 2)
                continue;

            files++;
            bytes += fi.Length;

            if (!sizeGroups.TryGetValue(fi.Length, out var list))
            {
                list = new List<FileEntry>();
                sizeGroups[fi.Length] = list;
            }

            list.Add(new FileEntry(fi.FullName, fi.Length));

            if (options.Progress is not null && files % interval == 0)
                options.Progress(new ScanProgress(ScanProgressPhase.CandidateScan, files, bytes, false));
        }

        options.Progress?.Invoke(new ScanProgress(ScanProgressPhase.CandidateScan, files, bytes, true));

        return BuildGroupsFromSizeGroups(sizeGroups, options.ExactMode, options.HashAlgorithm, options.CancellationToken);
    }

    private IEnumerable<DuplicateGroup> ScanSinglePass(ScanOptions options)
    {
        var sizeGroups = new Dictionary<long, List<FileEntry>>();
        long files = 0;
        long bytes = 0;
        int interval = options.ProgressInterval > 0 ? options.ProgressInterval : int.MaxValue;

        foreach (var fi in EnumerateCandidateFiles(options))
        {
            files++;
            bytes += fi.Length;

            if (!sizeGroups.TryGetValue(fi.Length, out var list))
            {
                list = new List<FileEntry>();
                sizeGroups[fi.Length] = list;
            }

            list.Add(new FileEntry(fi.FullName, fi.Length));

            if (options.Progress is not null && files % interval == 0)
                options.Progress(new ScanProgress(ScanProgressPhase.SinglePass, files, bytes, false));
        }

        options.Progress?.Invoke(new ScanProgress(ScanProgressPhase.SinglePass, files, bytes, true));

        return BuildGroupsFromSizeGroups(sizeGroups, options.ExactMode, options.HashAlgorithm, options.CancellationToken);
    }

    // ----------------- Candidate enumeration -----------------

    private IEnumerable<FileInfo> EnumerateCandidateFiles(ScanOptions options)
    {
        // Dedupe by full path so overlapping inputs (a dir plus a file inside it,
        // a dir plus a subdir, or the same path twice) never make a file a
        // "duplicate" of itself, which could otherwise plan a destructive action
        // whose target is the only copy. Comparison is case-insensitive on Windows.
        var seen = new HashSet<string>(PathComparer);

        foreach (var root in options.Paths)
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (File.Exists(root))
            {
                var fi = new FileInfo(root);
                if (IsCandidate(fi, options) && seen.Add(fi.FullName))
                    yield return fi;
            }
            else if (Directory.Exists(root))
            {
                foreach (var fi in EnumerateFromDirectory(new DirectoryInfo(root), options))
                {
                    if (seen.Add(fi.FullName))
                        yield return fi;
                }
            }
        }
    }

    /// <summary>
    /// Path comparer matching the host filesystem's case behaviour
    /// (case-insensitive on Windows, case-sensitive elsewhere).
    /// </summary>
    internal static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private IEnumerable<FileInfo> EnumerateFromDirectory(DirectoryInfo root, ScanOptions options)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            options.CancellationToken.ThrowIfCancellationRequested();

            var current = stack.Pop();

            if (options.IgnoredDirectoryNames.Contains(current.Name))
                continue;

            FileInfo[] files;
            try
            {
                files = current.GetFiles();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var fi in files)
            {
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (options.IgnoredFileNames.Contains(fi.Name))
                    continue;

                if (!IsCandidate(fi, options))
                    continue;

                yield return fi;
            }

            if (!options.Recursive)
                continue;

            DirectoryInfo[] subDirs;
            try
            {
                subDirs = current.GetDirectories();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                stack.Push(sub);
            }
        }
    }

    private static bool IsCandidate(FileInfo fi, ScanOptions options)
    {
        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;

        if (fi.Length < options.MinFileSizeBytes)
            return false;

        if (options.SafeExtensions.Count > 0 && !options.SafeExtensions.Contains(fi.Extension))
            return false;

        return true;
    }

    // ----------------- Grouping by size + hash -----------------

    private static IEnumerable<DuplicateGroup> BuildGroupsFromSizeGroups(
        Dictionary<long, List<FileEntry>> sizeGroups,
        ExactScanMode mode,
        HashAlgorithmKind hashAlgorithm,
        System.Threading.CancellationToken cancellationToken)
    {
        // A non-cryptographic hash only buckets candidates; buckets must then be
        // split by binary content. SHA-256 is trusted unless the caller asks for
        // explicit verification.
        bool verify = mode == ExactScanMode.HashWithBinaryVerification
                      || !IsCryptographic(hashAlgorithm);

        // Deterministic output: iterate sizes in ascending order, and order the
        // groups (and the files within each group) by path. A scan must give
        // reproducible duplicate groups regardless of dictionary/filesystem order.
        foreach (var kvp in sizeGroups.OrderBy(g => g.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var size = kvp.Key;
            var list = kvp.Value;

            if (list.Count < 2)
                continue;

            List<List<FileEntry>> rawGroups;

            if (mode == ExactScanMode.BinaryForPairs_HashForGroups && list.Count == 2)
            {
                // A pair is settled by a single binary compare; no hashing needed.
                rawGroups = FilesAreEqualBinary(list[0].Path, list[1].Path, cancellationToken)
                    ? new List<List<FileEntry>> { new() { list[0], list[1] } }
                    : new List<List<FileEntry>>();
            }
            else
            {
                rawGroups = new List<List<FileEntry>>();
                foreach (var bucket in GroupByHash(list, hashAlgorithm, cancellationToken).Where(b => b.Count >= 2))
                {
                    if (verify)
                        rawGroups.AddRange(PartitionByBinaryContent(bucket, cancellationToken));
                    else
                        rawGroups.Add(bucket);
                }
            }

            // Sort files within each group, drop non-groups, then order groups by first path.
            var finalized = rawGroups
                .Where(g => g.Count > 1)
                .Select(g => g.OrderBy(f => f.Path, PathComparer).ToList())
                .OrderBy(g => g[0].Path, PathComparer);

            foreach (var group in finalized)
                yield return new DuplicateGroup(size, group);
        }
    }

    private static bool IsCryptographic(HashAlgorithmKind algorithm) =>
        algorithm is HashAlgorithmKind.Sha256 or HashAlgorithmKind.Blake3;

    /// <summary>
    /// Splits a hash bucket into equivalence classes of binary-identical files.
    /// Each candidate is compared against the representative of each existing class,
    /// so genuine hash collisions (different content) form separate classes rather
    /// than being silently dropped.
    /// </summary>
    private static List<List<FileEntry>> PartitionByBinaryContent(
        List<FileEntry> bucket,
        System.Threading.CancellationToken cancellationToken)
    {
        var classes = new List<List<FileEntry>>();

        foreach (var f in bucket)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool placed = false;
            foreach (var cls in classes)
            {
                if (FilesAreEqualBinary(cls[0].Path, f.Path, cancellationToken))
                {
                    cls.Add(f);
                    placed = true;
                    break;
                }
            }

            if (!placed)
                classes.Add(new List<FileEntry> { f });
        }

        return classes;
    }

    private static List<List<FileEntry>> GroupByHash(
        List<FileEntry> files,
        HashAlgorithmKind hashAlgorithm,
        System.Threading.CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);

        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = ComputeHash(f.Path, hashAlgorithm, cancellationToken);
            var hashKey = Convert.ToHexString(hash);
            var withHash = f.WithHash(hash);

            if (!dict.TryGetValue(hashKey, out var list))
            {
                list = new List<FileEntry>();
                dict[hashKey] = list;
            }

            list.Add(withHash);
        }

        return dict.Values.ToList();
    }

    private static byte[] ComputeHash(
        string path,
        HashAlgorithmKind algorithm,
        System.Threading.CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;

        // We read in 1 MB chunks into a pooled buffer ourselves, so disable
        // FileStream's own internal buffer (bufferSize 1) to avoid a redundant
        // per-stream allocation.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.SequentialScan);

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            switch (algorithm)
            {
                case HashAlgorithmKind.Sha256:
                {
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    AppendStream(stream, buffer, hasher.AppendData, cancellationToken);
                    return hasher.GetHashAndReset();
                }

                case HashAlgorithmKind.XxHash3:
                {
                    var hasher = new XxHash3();
                    AppendStream(stream, buffer, (b, o, c) => hasher.Append(b.AsSpan(o, c)), cancellationToken);
                    return hasher.GetHashAndReset();
                }

                case HashAlgorithmKind.XxHash128:
                {
                    var hasher = new XxHash128();
                    AppendStream(stream, buffer, (b, o, c) => hasher.Append(b.AsSpan(o, c)), cancellationToken);
                    return hasher.GetHashAndReset();
                }

                case HashAlgorithmKind.Blake3:
                {
                    // Hasher is a struct, so it cannot be captured in the AppendStream
                    // lambda (that would mutate a copy). Drive the read loop inline.
                    using var hasher = Hasher.New();
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        hasher.Update(buffer.AsSpan(0, read));
                    }
                    return hasher.Finalize().AsSpan().ToArray();
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown HashAlgorithmKind.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void AppendStream(
        Stream stream,
        byte[] buffer,
        Action<byte[], int, int> append,
        System.Threading.CancellationToken cancellationToken)
    {
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            append(buffer, 0, read);
        }
    }

    private static bool FilesAreEqualBinary(
        string path1,
        string path2,
        System.Threading.CancellationToken cancellationToken = default)
    {
        const int bufferSize = 1024 * 1024;

        // Disable FileStream's internal buffer; reads go through our own pooled buffers.
        using var fs1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.SequentialScan);
        using var fs2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.SequentialScan);

        if (fs1.Length != fs2.Length)
            return false;

        var buffer1 = ArrayPool<byte>.Shared.Rent(bufferSize);
        var buffer2 = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            long remaining = fs1.Length;

            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(bufferSize, remaining);
                fs1.ReadExactly(buffer1, 0, toRead);
                fs2.ReadExactly(buffer2, 0, toRead);

                if (!buffer1.AsSpan(0, toRead).SequenceEqual(buffer2.AsSpan(0, toRead)))
                    return false;

                remaining -= toRead;
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer1);
            ArrayPool<byte>.Shared.Return(buffer2);
        }
    }
}
