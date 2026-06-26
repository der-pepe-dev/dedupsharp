using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DedupSharp.Core;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace DedupSharp.Tests;

public class DuplicateActionApplierTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _quarantineDir;

    public DuplicateActionApplierTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DedupSharpApplierTests_" + Guid.NewGuid().ToString("N"));
        _quarantineDir = Path.Combine(Path.GetTempPath(), "DedupSharpQuarantine_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_quarantineDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        if (Directory.Exists(_quarantineDir)) Directory.Delete(_quarantineDir, recursive: true);
    }

    // ---------- helpers ----------

    private string WriteFile(string name, string content = "content")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private DupAction MakeMoveAction(string canonical, string target) => new()
    {
        Kind = DupActionKind.MoveToQuarantine,
        CanonicalPath = canonical,
        TargetPath = target,
        SizeBytes = new FileInfo(target).Length,
        TargetOriginalSizeBytes = new FileInfo(target).Length,
        TargetOriginalLastWriteTimeUtc = new FileInfo(target).LastWriteTimeUtc,
        CanonicalOriginalSizeBytes = new FileInfo(canonical).Length,
        CanonicalOriginalLastWriteTimeUtc = new FileInfo(canonical).LastWriteTimeUtc
    };

    private DupAction MakeDeleteAction(string canonical, string target) => new()
    {
        Kind = DupActionKind.Delete,
        CanonicalPath = canonical,
        TargetPath = target,
        SizeBytes = new FileInfo(target).Length,
        TargetOriginalSizeBytes = new FileInfo(target).Length,
        TargetOriginalLastWriteTimeUtc = new FileInfo(target).LastWriteTimeUtc,
        CanonicalOriginalSizeBytes = new FileInfo(canonical).Length,
        CanonicalOriginalLastWriteTimeUtc = new FileInfo(canonical).LastWriteTimeUtc
    };

    // ---------- MoveToQuarantine ----------

    [Test]
    public async Task MoveToQuarantine_DryRun_DoesNotMoveFile()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = true, QuarantineDirectory = _quarantineDir };

        var result = DuplicateActionApplier.Apply([action], options);

        await Assert.That(File.Exists(target)).IsTrue();
        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(result.DryRunApplied).IsEqualTo(1);
        await Assert.That(result.Skipped).IsEqualTo(0);
    }

    [Test]
    public async Task MoveToQuarantine_NoQuarantineDir_SkipsNotFails()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = null };

        var result = DuplicateActionApplier.Apply([action], options);

        await Assert.That(File.Exists(target)).IsTrue();
        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(result.Failed).IsEqualTo(0);
    }

    [Test]
    public async Task MoveToQuarantine_NoDryRun_MovesFile()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = _quarantineDir };

        var result = DuplicateActionApplier.Apply([action], options);

        await Assert.That(File.Exists(target)).IsFalse();
        await Assert.That(Directory.GetFiles(_quarantineDir)).HasSingleItem();
        await Assert.That(result.Applied).IsEqualTo(1);
        await Assert.That(result.Skipped).IsEqualTo(0);
    }

    [Test]
    public async Task MoveToQuarantine_UniqueDestination_WhenNameCollides()
    {
        var canonical = WriteFile("canonical.txt");
        var dup1 = WriteFile("dup.txt", "same");
        var dup2Path = Path.Combine(_tempDir, "dup2.txt");
        File.WriteAllText(dup2Path, "same");

        // Pre-place a file in quarantine with the dup name so a collision occurs
        File.WriteAllText(Path.Combine(_quarantineDir, "dup.txt"), "old");

        var action = MakeMoveAction(canonical, dup1);
        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = _quarantineDir };

        DuplicateActionApplier.Apply([action], options);

        // Original dup.txt in quarantine should still exist; moved file gets unique name
        await Assert.That(Directory.GetFiles(_quarantineDir).Length).IsEqualTo(2);
    }

    // ---------- Delete ----------

    [Test]
    public async Task Delete_AllowDeleteFalse_SkipsNotFails()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeDeleteAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, AllowDelete = false };

        var result = DuplicateActionApplier.Apply([action], options);

        await Assert.That(File.Exists(target)).IsTrue();
        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
        await Assert.That(result.Failed).IsEqualTo(0);
    }

    [Test]
    public async Task Delete_DryRun_DoesNotDeleteFile()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeDeleteAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = true, AllowDelete = true };

        var result = DuplicateActionApplier.Apply([action], options);

        await Assert.That(File.Exists(target)).IsTrue();
        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(result.DryRunApplied).IsEqualTo(1);
    }

    [Test]
    public async Task Delete_NoDryRun_DeletesTarget()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeDeleteAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, AllowDelete = true };

        var result = DuplicateActionApplier.Apply([action], options);

        await Assert.That(File.Exists(target)).IsFalse();
        await Assert.That(File.Exists(canonical)).IsTrue();
        await Assert.That(result.Applied).IsEqualTo(1);
    }

    // ---------- Drift detection ----------

    [Test]
    public async Task DriftDetection_ModifiedTarget_SkipsAction()
    {
        var canonical = WriteFile("canonical.txt", "original");
        var target = WriteFile("dup.txt", "original");

        var action = MakeMoveAction(canonical, target);

        // Simulate drift: overwrite target after planning
        File.WriteAllText(target, "changed content!!!");

        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = _quarantineDir };
        var result = DuplicateActionApplier.Apply([action], options);

        await Assert.That(File.Exists(target)).IsTrue();
        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(result.Skipped).IsEqualTo(1);
    }

    // ---------- Cancellation ----------

    [Test]
    public async Task Apply_CancelledToken_ThrowsOperationCancelled()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var options = new DuplicateActionApplyOptions { DryRun = true, QuarantineDirectory = _quarantineDir };

        await Assert.That(() => DuplicateActionApplier.Apply([action], options, cancellationToken: cts.Token))
            .Throws<OperationCanceledException>();
    }

    // ---------- result counts ----------

    [Test]
    public async Task Result_MultipleActions_CountsCorrectly()
    {
        var canonical = WriteFile("c.txt", "data");
        var dup1 = WriteFile("d1.txt", "data");
        var dup2 = WriteFile("d2.txt", "data");

        var actions = new[]
        {
            MakeMoveAction(canonical, dup1),
            MakeMoveAction(canonical, dup2)
        };
        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = _quarantineDir };

        var result = DuplicateActionApplier.Apply(actions, options);

        await Assert.That(result.TotalActions).IsEqualTo(2);
        await Assert.That(result.Applied).IsEqualTo(2);
        await Assert.That(result.Skipped).IsEqualTo(0);
        await Assert.That(result.Failed).IsEqualTo(0);
    }
}
