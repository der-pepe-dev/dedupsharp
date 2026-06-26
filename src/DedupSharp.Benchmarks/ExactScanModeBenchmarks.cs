using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using DedupSharp.Core;
using DedupSharp.Core.Exact;

namespace DedupSharp.Benchmarks;

/// <summary>
/// Compares the three <see cref="ExactScanMode"/> strategies over a generated
/// file tree containing a mix of unique files, duplicate pairs, and larger
/// duplicate groups across several sizes. This exercises the size pre-scan,
/// the early-out binary compare for pairs, and SHA-256 grouping for groups > 2.
/// </summary>
[MemoryDiagnoser]
public class ExactScanModeBenchmarks
{
    private string _root = string.Empty;
    private readonly ExactDuplicateScanner _scanner = new();

    [Params(
        ExactScanMode.HashOnly,
        ExactScanMode.BinaryForPairs_HashForGroups,
        ExactScanMode.HashWithBinaryVerification)]
    public ExactScanMode Mode;

    [Params(
        HashAlgorithmKind.Sha256,
        HashAlgorithmKind.XxHash3,
        HashAlgorithmKind.XxHash128,
        HashAlgorithmKind.Blake3)]
    public HashAlgorithmKind HashAlgorithm;

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "DedupSharpBench_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        // Vary content size so small/medium/large code paths all get hit.
        foreach (var sizeKb in new[] { 4, 64, 512 })
        {
            var payload = new string('x', sizeKb * 1024);

            // Unique files (no group).
            for (int i = 0; i < 20; i++)
                Write($"u_{sizeKb}_{i}.bin", payload + "_unique_" + i);

            // Duplicate pairs (binary-compare path).
            for (int i = 0; i < 20; i++)
            {
                var dup = payload + "_pair_" + i;
                Write($"p_{sizeKb}_{i}_a.bin", dup);
                Write($"p_{sizeKb}_{i}_b.bin", dup);
            }

            // Larger duplicate groups (hash-grouping path).
            for (int g = 0; g < 10; g++)
            {
                var dup = payload + "_group_" + g;
                for (int m = 0; m < 4; m++)
                    Write($"g_{sizeKb}_{g}_{m}.bin", dup);
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Benchmark]
    public int Scan()
    {
        var options = new ScanOptions
        {
            Paths = [_root],
            Recursive = true,
            UsePreScan = true,
            ExactMode = Mode,
            HashAlgorithm = HashAlgorithm
        };

        return _scanner.Scan(options).Count();
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_root, name), content);
}
