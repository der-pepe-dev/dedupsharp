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
            {
                options.Progress(new ScanProgress(ScanProgressPhase.PreScan, preFiles, preBytes, false));
            }
        }

        if (options.Progress is not null)
        {
            options.Progress(new ScanProgress(ScanProgressPhase.PreScan, preFiles, preBytes, true));
        }

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
            {
                options.Progress(new ScanProgress(ScanProgressPhase.SinglePass, files, bytes, false));
            }
        }

        if (options.Progress is not null)
        {
            options.Progress(new ScanProgress(ScanProgressPhase.SinglePass, files, bytes, true));
        }

        return BuildGroupsFromSizeGroups(sizeGroups, options.ExactMode);
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
            {
                options.Progress(new ScanProgress(ScanProgressPhase.SinglePass, files, bytes, false));
            }
        }

        if (options.Progress is not null)
        {
            options.Progress(new ScanProgress(ScanProgressPhase.SinglePass, files, bytes, true));
        }

        return BuildGroupsFromSizeGroups(sizeGroups, options.ExactMode);
    }

    // ----------------- Candidate enumeration -----------------

    private IEnumerable<FileInfo> EnumerateCandidateFiles(ScanOptions options)
    {
        foreach (var root in options.Paths)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (File.Exists(root))
            {
                var fi = new FileInfo(root);
                if (IsCandidate(fi, options))
                    yield return fi;
            }
            else if (Directory.Exists(root))
            {
                foreach (var fi in EnumerateFromDirectory(new DirectoryInfo(root), options))
                    yield return fi;
            }
        }
    }

    private IEnumerable<FileInfo> EnumerateFromDirectory(DirectoryInfo root, ScanOptions options)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (options.IgnoredDirectoryNames.Contains(current.Name))
                continue;

            FileInfo[] files;
            try
            {
                files = current.GetFiles();
            }
            catch
            {
                continue;
            }

            foreach (var fi in files)
            {
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
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                stack.Push(sub);
            }
        }
    }

    private static bool IsCandidate(FileInfo fi, ScanOptions options)
    {
        if (fi.Length < options.MinFileSizeBytes)
            return false;

        if (options.SafeExtensions.Count > 0)
        {
            // Only allow files whose extension is in SafeExtensions
            if (!options.SafeExtensions.Contains(fi.Extension))
                return false;
        }

        return true;
    }

    // ----------------- Grouping by size + hash -----------------

    private static IEnumerable<DuplicateGroup> BuildGroupsFromSizeGroups(
        Dictionary<long, List<FileEntry>> sizeGroups,
        ExactScanMode mode)
    {
        foreach (var kvp in sizeGroups)
        {
            var size = kvp.Key;
            var list = kvp.Value;

            if (list.Count < 2)
                continue;

            switch (mode)
            {
                case ExactScanMode.HashOnly:
                    foreach (var group in GroupByHash(list))
                    {
                        if (group.Count > 1)
                            yield return new DuplicateGroup(size, group);
                    }
                    break;

                case ExactScanMode.BinaryForPairs_HashForGroups:
                    if (list.Count == 2)
                    {
                        var a = list[0];
                        var b = list[1];

                        if (FilesAreEqualBinary(a.Path, b.Path))
                        {
                            yield return new DuplicateGroup(size, new[] { a, b });
                        }
                    }
                    else
                    {
                        foreach (var group in GroupByHash(list))
                        {
                            if (group.Count > 1)
                                yield return new DuplicateGroup(size, group);
                        }
                    }
                    break;

                case ExactScanMode.HashWithBinaryVerification:
                    foreach (var hashGroup in GroupByHash(list))
                    {
                        if (hashGroup.Count < 2)
                            continue;

                        var canonical = hashGroup[0];
                        var confirmed = new List<FileEntry> { canonical };

                        for (int i = 1; i < hashGroup.Count; i++)
                        {
                            var candidate = hashGroup[i];
                            if (FilesAreEqualBinary(canonical.Path, candidate.Path))
                            {
                                confirmed.Add(candidate);
                            }
                        }

                        if (confirmed.Count > 1)
                            yield return new DuplicateGroup(size, confirmed);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ExactScanMode.");
            }
        }
    }

    private static List<List<FileEntry>> GroupByHash(List<FileEntry> files)
    {
        var dict = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);

        foreach (var f in files)
        {
            var hash = ComputeSha256(f.Path);
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

    private static byte[] ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        return sha.ComputeHash(stream);
    }

    private static bool FilesAreEqualBinary(string path1, string path2)
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

        while (true)
        {
            var read1 = fs1.Read(buffer1, 0, buffer1.Length);
            var read2 = fs2.Read(buffer2, 0, buffer2.Length);

            if (read1 != read2)
                return false;

            if (read1 == 0)
                break;

            for (int i = 0; i < read1; i++)
            {
                if (buffer1[i] != buffer2[i])
                    return false;
            }
        }

        return true;
    }
}
