using System;
using System.IO;
using System.Linq;
using DedupSharp.Core;
using DedupSharp.Core.Exact;
using Xunit;

namespace DedupSharp.Tests;

public class ExactDuplicateScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExactDuplicateScanner _scanner = new();

    public ExactDuplicateScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DedupSharpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ---------- helpers ----------

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private ScanOptions SingleDirOptions(bool usePreScan = true) => new()
    {
        Paths = [_tempDir],
        Recursive = true,
        UsePreScan = usePreScan
    };

    // ---------- basic ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scanner_FindsIdenticalFiles(bool usePreScan)
    {
        WriteFile("a.txt", "hello world");
        WriteFile("b.txt", "hello world");

        var groups = _scanner.Scan(SingleDirOptions(usePreScan)).ToList();

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Files.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scanner_NoGroupsForUniqueFiles(bool usePreScan)
    {
        WriteFile("a.txt", "aaa");
        WriteFile("b.txt", "bbb");

        var groups = _scanner.Scan(SingleDirOptions(usePreScan)).ToList();

        Assert.Empty(groups);
    }

    [Fact]
    public void Scanner_ThreeIdenticalFiles_OneGroup()
    {
        WriteFile("x.txt", "same");
        WriteFile("y.txt", "same");
        WriteFile("z.txt", "same");

        var groups = _scanner.Scan(SingleDirOptions()).ToList();

        Assert.Single(groups);
        Assert.Equal(3, groups[0].Files.Count);
    }

    [Fact]
    public void Scanner_TwoPairsOfDuplicates_TwoGroups()
    {
        WriteFile("a1.txt", "content-alpha");
        WriteFile("a2.txt", "content-alpha");
        WriteFile("b1.txt", "content-beta");
        WriteFile("b2.txt", "content-beta");

        var groups = _scanner.Scan(SingleDirOptions()).ToList();

        Assert.Equal(2, groups.Count);
    }

    // ---------- extension filter ----------

    [Fact]
    public void Scanner_ExtensionFilter_SkipsNonMatchingExtensions()
    {
        WriteFile("dup.txt", "same");
        WriteFile("dup.log", "same");

        var options = SingleDirOptions();
        options.SafeExtensions.Add(".txt");

        var groups = _scanner.Scan(options).ToList();

        Assert.Empty(groups); // only one .txt, can't form group
    }

    [Fact]
    public void Scanner_ExtensionFilter_FindsMatchingDuplicates()
    {
        WriteFile("a.txt", "same");
        WriteFile("b.txt", "same");
        WriteFile("c.log", "same"); // excluded

        var options = SingleDirOptions();
        options.SafeExtensions.Add(".txt");

        var groups = _scanner.Scan(options).ToList();

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Files.Count);
        Assert.All(groups[0].Files, f => Assert.EndsWith(".txt", f.Path, StringComparison.OrdinalIgnoreCase));
    }

    // ---------- min size ----------

    [Fact]
    public void Scanner_MinSize_SkipsSmallFiles()
    {
        WriteFile("small1.txt", "hi"); // 2 bytes
        WriteFile("small2.txt", "hi");

        var options = SingleDirOptions();
        options.MinFileSizeBytes = 100;

        var groups = _scanner.Scan(options).ToList();

        Assert.Empty(groups);
    }

    // ---------- ignored dirs ----------

    [Fact]
    public void Scanner_IgnoredDirectory_IsSkipped()
    {
        var subDir = Path.Combine(_tempDir, "skip_me");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "same");
        File.WriteAllText(Path.Combine(subDir, "b.txt"), "same");

        var options = SingleDirOptions();
        options.IgnoredDirectoryNames.Add("skip_me");

        var groups = _scanner.Scan(options).ToList();

        Assert.Empty(groups);
    }

    // ---------- exact modes ----------

    [Theory]
    [InlineData(ExactScanMode.HashOnly)]
    [InlineData(ExactScanMode.BinaryForPairs_HashForGroups)]
    [InlineData(ExactScanMode.HashWithBinaryVerification)]
    public void Scanner_AllModes_FindDuplicatePair(ExactScanMode mode)
    {
        WriteFile("p.bin", "duplicate content here");
        WriteFile("q.bin", "duplicate content here");

        var options = SingleDirOptions();
        options.ExactMode = mode;

        var groups = _scanner.Scan(options).ToList();

        Assert.Single(groups);
    }

    [Theory]
    [InlineData(ExactScanMode.HashOnly)]
    [InlineData(ExactScanMode.BinaryForPairs_HashForGroups)]
    [InlineData(ExactScanMode.HashWithBinaryVerification)]
    public void Scanner_AllModes_NoDuplicates(ExactScanMode mode)
    {
        WriteFile("p.bin", "aaa");
        WriteFile("q.bin", "bbb");

        var options = SingleDirOptions();
        options.ExactMode = mode;

        var groups = _scanner.Scan(options).ToList();

        Assert.Empty(groups);
    }

    // ---------- group size ----------

    [Fact]
    public void Scanner_GroupSizeBytes_MatchesActualFileSize()
    {
        const string content = "exactly this content";
        WriteFile("f1.txt", content);
        WriteFile("f2.txt", content);

        var expectedSize = new FileInfo(Path.Combine(_tempDir, "f1.txt")).Length;
        var groups = _scanner.Scan(SingleDirOptions()).ToList();

        Assert.Single(groups);
        Assert.Equal(expectedSize, groups[0].SizeBytes);
    }

    // ---------- validation ----------

    [Fact]
    public void Scanner_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _scanner.Scan(null!).ToList());
    }

    [Fact]
    public void Scanner_EmptyPaths_Throws()
    {
        Assert.Throws<ArgumentException>(() => _scanner.Scan(new ScanOptions()).ToList());
    }

    // ---------- cancellation ----------

    [Fact]
    public void Scanner_CancelledToken_ThrowsOperationCancelled()
    {
        for (int i = 0; i < 20; i++)
            WriteFile($"file{i}.txt", "same content everywhere");

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var options = SingleDirOptions();
        options.CancellationToken = cts.Token;

        Assert.Throws<OperationCanceledException>(() => _scanner.Scan(options).ToList());
    }
}
