using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DedupSharp.Core;

namespace DedupSharp.Core.Media;

/// <summary>
/// Near-duplicate image scanner: computes a perceptual hash per image and clusters images
/// whose hashes are within <see cref="ScanOptions.HammingThreshold"/> of each other.
/// Unlike the exact engine, groups are visually-similar images, not byte-identical files.
/// </summary>
public sealed class MediaImageScanner : IDuplicateScanner
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public IEnumerable<DuplicateGroup> Scan(ScanOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.Paths is null || options.Paths.Count == 0)
            throw new ArgumentException("At least one path must be specified.", nameof(options));
        if (options.HammingThreshold is < 0 or > 64)
            throw new ArgumentException("HammingThreshold must be between 0 and 64.", nameof(options));

        var token = options.CancellationToken;

        // Hash every image (skip ones that fail to decode). Ordered by path for determinism.
        var items = new List<(FileEntry Entry, ulong Hash)>();
        foreach (var fi in EnumerateImageFiles(options).OrderBy(f => f.FullName, PathComparer))
        {
            token.ThrowIfCancellationRequested();

            ulong hash;
            try
            {
                hash = PerceptualHasher.Compute(fi.FullName, options.PerceptualHash);
            }
            catch
            {
                continue; // not a decodable image
            }

            items.Add((new FileEntry(fi.FullName, fi.Length), hash));
        }

        return Cluster(items, options.HammingThreshold, token);
    }

    private static IEnumerable<DuplicateGroup> Cluster(
        List<(FileEntry Entry, ulong Hash)> items,
        int threshold,
        System.Threading.CancellationToken token)
    {
        int n = items.Count;
        var uf = new UnionFind(n);

        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            for (int j = i + 1; j < n; j++)
            {
                if (PerceptualHasher.Distance(items[i].Hash, items[j].Hash) <= threshold)
                    uf.Union(i, j);
            }
        }

        // Gather clusters, keep size > 1, sort files by path, order groups by first path.
        var clusters = new Dictionary<int, List<FileEntry>>();
        for (int i = 0; i < n; i++)
        {
            int root = uf.Find(i);
            if (!clusters.TryGetValue(root, out var list))
                clusters[root] = list = new List<FileEntry>();
            list.Add(items[i].Entry);
        }

        return clusters.Values
            .Where(c => c.Count > 1)
            .Select(c => c.OrderBy(f => f.Path, PathComparer).ToList())
            .OrderBy(c => c[0].Path, PathComparer)
            .Select(c => new DuplicateGroup(c[0].Size, c, DuplicateKind.MediaImage))
            .ToList();
    }

    private static IEnumerable<FileInfo> EnumerateImageFiles(ScanOptions options)
    {
        var seen = new HashSet<string>(PathComparer);

        foreach (var root in options.Paths)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root))
                continue;

            if (File.Exists(root))
            {
                var fi = new FileInfo(root);
                if (IsImageCandidate(fi, options) && seen.Add(fi.FullName))
                    yield return fi;
            }
            else if (Directory.Exists(root))
            {
                foreach (var fi in EnumerateFromDirectory(new DirectoryInfo(root), options)
                             .Where(fi => seen.Add(fi.FullName)))
                    yield return fi;
            }
        }
    }

    private static IEnumerable<FileInfo> EnumerateFromDirectory(DirectoryInfo root, ScanOptions options)
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
            try { files = current.GetFiles(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }

            foreach (var fi in files.Where(fi =>
                         (fi.Attributes & FileAttributes.ReparsePoint) == 0 &&
                         !options.IgnoredFileNames.Contains(fi.Name) &&
                         IsImageCandidate(fi, options)))
            {
                yield return fi;
            }

            if (!options.Recursive)
                continue;

            DirectoryInfo[] subDirs;
            try { subDirs = current.GetDirectories(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }

            foreach (var sub in subDirs)
                if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                    stack.Push(sub);
        }
    }

    private static bool IsImageCandidate(FileInfo fi, ScanOptions options)
    {
        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            return false;
        if (fi.Length < options.MinFileSizeBytes)
            return false;
        return options.ImageExtensions.Contains(fi.Extension);
    }

    /// <summary>Disjoint-set (union by rank, path compression).</summary>
    private sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public UnionFind(int n)
        {
            _parent = new int[n];
            _rank = new int[n];
            for (int i = 0; i < n; i++) _parent[i] = i;
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }
            return x;
        }

        public void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;
            if (_rank[ra] < _rank[rb]) (ra, rb) = (rb, ra);
            _parent[rb] = ra;
            if (_rank[ra] == _rank[rb]) _rank[ra]++;
        }
    }
}
