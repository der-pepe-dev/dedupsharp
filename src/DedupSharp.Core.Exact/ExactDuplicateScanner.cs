using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

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

        return BuildGroupsFromSizeGroups(sizeGroups, options.ExactMode, options.CancellationToken);
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

        return BuildGroupsFromSizeGroups(sizeGroups, options.ExactMode, options.CancellationToken);
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
        System.Threading.CancellationToken cancellationToken)
    {
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

            var rawGroups = mode switch
            {
                ExactScanMode.HashOnly =>
                    GroupByHash(list, cancellationToken),

                ExactScanMode.BinaryForPairs_HashForGroups =>
                    BinaryForPairsHashForGroups(list, cancellationToken),

                ExactScanMode.HashWithBinaryVerification =>
                    HashWithBinaryVerification(list, cancellationToken),

                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ExactScanMode.")
            };

            // Sort files within each group, drop non-groups, then order groups by first path.
            var finalized = rawGroups
                .Where(g => g.Count > 1)
                .Select(g => g.OrderBy(f => f.Path, PathComparer).ToList())
                .OrderBy(g => g[0].Path, PathComparer);

            foreach (var group in finalized)
                yield return new DuplicateGroup(size, group);
        }
    }

    private static List<List<FileEntry>> BinaryForPairsHashForGroups(
        List<FileEntry> list,
        System.Threading.CancellationToken cancellationToken)
    {
        if (list.Count == 2)
        {
            return FilesAreEqualBinary(list[0].Path, list[1].Path, cancellationToken)
                ? new List<List<FileEntry>> { new() { list[0], list[1] } }
                : new List<List<FileEntry>>();
        }

        return GroupByHash(list, cancellationToken);
    }

    private static List<List<FileEntry>> HashWithBinaryVerification(
        List<FileEntry> list,
        System.Threading.CancellationToken cancellationToken)
    {
        var result = new List<List<FileEntry>>();

        foreach (var hashGroup in GroupByHash(list, cancellationToken))
        {
            if (hashGroup.Count < 2)
                continue;

            var canonical = hashGroup[0];
            var confirmed = new List<FileEntry> { canonical };

            for (int i = 1; i < hashGroup.Count; i++)
            {
                var candidate = hashGroup[i];
                if (FilesAreEqualBinary(canonical.Path, candidate.Path, cancellationToken))
                    confirmed.Add(candidate);
            }

            if (confirmed.Count > 1)
                result.Add(confirmed);
        }

        return result;
    }

    private static List<List<FileEntry>> GroupByHash(
        List<FileEntry> files,
        System.Threading.CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);

        foreach (var f in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = ComputeSha256(f.Path, cancellationToken);
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

    private static byte[] ComputeSha256(string path, System.Threading.CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.SequentialScan);

        var buffer = new byte[bufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, bufferSize)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
        }

        return hasher.GetHashAndReset();
    }

    private static bool FilesAreEqualBinary(
        string path1,
        string path2,
        System.Threading.CancellationToken cancellationToken = default)
    {
        const int bufferSize = 1024 * 1024;

        using var fs1 = new FileStream(path1, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.SequentialScan);
        using var fs2 = new FileStream(path2, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.SequentialScan);

        if (fs1.Length != fs2.Length)
            return false;

        var buffer1 = new byte[bufferSize];
        var buffer2 = new byte[bufferSize];
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
}
