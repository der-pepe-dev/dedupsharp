using System;
using System.IO;
using System.Linq;
using DedupSharp.Core;
using Xunit;

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

    [Fact]
    public void MoveToQuarantine_DryRun_DoesNotMoveFile()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = true, QuarantineDirectory = _quarantineDir };

        var result = DuplicateActionApplier.Apply([action], options);

        Assert.True(File.Exists(target));
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.DryRunApplied);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void MoveToQuarantine_NoQuarantineDir_SkipsNotFails()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = null };

        var result = DuplicateActionApplier.Apply([action], options);

        Assert.True(File.Exists(target));
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void MoveToQuarantine_NoDryRun_MovesFile()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = _quarantineDir };

        var result = DuplicateActionApplier.Apply([action], options);

        Assert.False(File.Exists(target));
        Assert.Single(Directory.GetFiles(_quarantineDir));
        Assert.Equal(1, result.Applied);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void MoveToQuarantine_UniqueDestination_WhenNameCollides()
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
        Assert.Equal(2, Directory.GetFiles(_quarantineDir).Length);
    }

    // ---------- Delete ----------

    [Fact]
    public void Delete_AllowDeleteFalse_SkipsNotFails()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeDeleteAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, AllowDelete = false };

        var result = DuplicateActionApplier.Apply([action], options);

        Assert.True(File.Exists(target));
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void Delete_DryRun_DoesNotDeleteFile()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeDeleteAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = true, AllowDelete = true };

        var result = DuplicateActionApplier.Apply([action], options);

        Assert.True(File.Exists(target));
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.DryRunApplied);
    }

    [Fact]
    public void Delete_NoDryRun_DeletesTarget()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeDeleteAction(canonical, target);
        var options = new DuplicateActionApplyOptions { DryRun = false, AllowDelete = true };

        var result = DuplicateActionApplier.Apply([action], options);

        Assert.False(File.Exists(target));
        Assert.True(File.Exists(canonical));
        Assert.Equal(1, result.Applied);
    }

    // ---------- Drift detection ----------

    [Fact]
    public void DriftDetection_ModifiedTarget_SkipsAction()
    {
        var canonical = WriteFile("canonical.txt", "original");
        var target = WriteFile("dup.txt", "original");

        var action = MakeMoveAction(canonical, target);

        // Simulate drift: overwrite target after planning
        File.WriteAllText(target, "changed content!!!");

        var options = new DuplicateActionApplyOptions { DryRun = false, QuarantineDirectory = _quarantineDir };
        var result = DuplicateActionApplier.Apply([action], options);

        Assert.True(File.Exists(target));
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Skipped);
    }

    // ---------- Cancellation ----------

    [Fact]
    public void Apply_CancelledToken_ThrowsOperationCancelled()
    {
        var canonical = WriteFile("canonical.txt");
        var target = WriteFile("dup.txt");

        var action = MakeMoveAction(canonical, target);

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var options = new DuplicateActionApplyOptions { DryRun = true, QuarantineDirectory = _quarantineDir };

        Assert.Throws<OperationCanceledException>(
            () => DuplicateActionApplier.Apply([action], options, cancellationToken: cts.Token));
    }

    // ---------- result counts ----------

    [Fact]
    public void Result_MultipleActions_CountsCorrectly()
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

        Assert.Equal(2, result.TotalActions);
        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
    }
}
