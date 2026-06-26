using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DedupSharp.Core;
using DedupSharp.Core.Exact;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

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

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Scanner_FindsIdenticalFiles(bool usePreScan)
    {
        WriteFile("a.txt", "hello world");
        WriteFile("b.txt", "hello world");

        var groups = _scanner.Scan(SingleDirOptions(usePreScan)).ToList();

        await Assert.That(groups).HasSingleItem();
        await Assert.That(groups[0].Files.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Scanner_NoGroupsForUniqueFiles(bool usePreScan)
    {
        WriteFile("a.txt", "aaa");
        WriteFile("b.txt", "bbb");

        var groups = _scanner.Scan(SingleDirOptions(usePreScan)).ToList();

        await Assert.That(groups).IsEmpty();
    }

    [Test]
    public async Task Scanner_ThreeIdenticalFiles_OneGroup()
    {
        WriteFile("x.txt", "same");
        WriteFile("y.txt", "same");
        WriteFile("z.txt", "same");

        var groups = _scanner.Scan(SingleDirOptions()).ToList();

        await Assert.That(groups).HasSingleItem();
        await Assert.That(groups[0].Files.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Scanner_TwoPairsOfDuplicates_TwoGroups()
    {
        WriteFile("a1.txt", "content-alpha");
        WriteFile("a2.txt", "content-alpha");
        WriteFile("b1.txt", "content-beta");
        WriteFile("b2.txt", "content-beta");

        var groups = _scanner.Scan(SingleDirOptions()).ToList();

        await Assert.That(groups.Count).IsEqualTo(2);
    }

    // ---------- extension filter ----------

    [Test]
    public async Task Scanner_ExtensionFilter_SkipsNonMatchingExtensions()
    {
        WriteFile("dup.txt", "same");
        WriteFile("dup.log", "same");

        var options = SingleDirOptions();
        options.SafeExtensions.Add(".txt");

        var groups = _scanner.Scan(options).ToList();

        await Assert.That(groups).IsEmpty(); // only one .txt, can't form group
    }

    [Test]
    public async Task Scanner_ExtensionFilter_FindsMatchingDuplicates()
    {
        WriteFile("a.txt", "same");
        WriteFile("b.txt", "same");
        WriteFile("c.log", "same"); // excluded

        var options = SingleDirOptions();
        options.SafeExtensions.Add(".txt");

        var groups = _scanner.Scan(options).ToList();

        await Assert.That(groups).HasSingleItem();
        await Assert.That(groups[0].Files.Count).IsEqualTo(2);
        foreach (var f in groups[0].Files)
            await Assert.That(f.Path).EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    // ---------- min size ----------

    [Test]
    public async Task Scanner_MinSize_SkipsSmallFiles()
    {
        WriteFile("small1.txt", "hi"); // 2 bytes
        WriteFile("small2.txt", "hi");

        var options = SingleDirOptions();
        options.MinFileSizeBytes = 100;

        var groups = _scanner.Scan(options).ToList();

        await Assert.That(groups).IsEmpty();
    }

    // ---------- ignored dirs ----------

    [Test]
    public async Task Scanner_IgnoredDirectory_IsSkipped()
    {
        var subDir = Path.Combine(_tempDir, "skip_me");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "same");
        File.WriteAllText(Path.Combine(subDir, "b.txt"), "same");

        var options = SingleDirOptions();
        options.IgnoredDirectoryNames.Add("skip_me");

        var groups = _scanner.Scan(options).ToList();

        await Assert.That(groups).IsEmpty();
    }

    // ---------- exact modes ----------

    [Test]
    [Arguments(ExactScanMode.HashOnly)]
    [Arguments(ExactScanMode.BinaryForPairs_HashForGroups)]
    [Arguments(ExactScanMode.HashWithBinaryVerification)]
    public async Task Scanner_AllModes_FindDuplicatePair(ExactScanMode mode)
    {
        WriteFile("p.bin", "duplicate content here");
        WriteFile("q.bin", "duplicate content here");

        var options = SingleDirOptions();
        options.ExactMode = mode;

        var groups = _scanner.Scan(options).ToList();

        await Assert.That(groups).HasSingleItem();
    }

    [Test]
    [Arguments(ExactScanMode.HashOnly)]
    [Arguments(ExactScanMode.BinaryForPairs_HashForGroups)]
    [Arguments(ExactScanMode.HashWithBinaryVerification)]
    public async Task Scanner_AllModes_NoDuplicates(ExactScanMode mode)
    {
        WriteFile("p.bin", "aaa");
        WriteFile("q.bin", "bbb");

        var options = SingleDirOptions();
        options.ExactMode = mode;

        var groups = _scanner.Scan(options).ToList();

        await Assert.That(groups).IsEmpty();
    }

    // ---------- group size ----------

    [Test]
    public async Task Scanner_GroupSizeBytes_MatchesActualFileSize()
    {
        const string content = "exactly this content";
        WriteFile("f1.txt", content);
        WriteFile("f2.txt", content);

        var expectedSize = new FileInfo(Path.Combine(_tempDir, "f1.txt")).Length;
        var groups = _scanner.Scan(SingleDirOptions()).ToList();

        await Assert.That(groups).HasSingleItem();
        await Assert.That(groups[0].SizeBytes).IsEqualTo(expectedSize);
    }

    // ---------- validation ----------

    [Test]
    public async Task Scanner_NullOptions_Throws()
    {
        await Assert.That(() => _scanner.Scan(null!).ToList()).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Scanner_EmptyPaths_Throws()
    {
        await Assert.That(() => _scanner.Scan(new ScanOptions()).ToList()).Throws<ArgumentException>();
    }

    // ---------- cancellation ----------

    [Test]
    public async Task Scanner_CancelledToken_ThrowsOperationCancelled()
    {
        for (int i = 0; i < 20; i++)
            WriteFile($"file{i}.txt", "same content everywhere");

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var options = SingleDirOptions();
        options.CancellationToken = cts.Token;

        await Assert.That(() => _scanner.Scan(options).ToList()).Throws<OperationCanceledException>();
    }
}
