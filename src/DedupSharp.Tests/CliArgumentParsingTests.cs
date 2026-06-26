using System;
using System.Linq;
using System.Threading.Tasks;
using DedupSharp.Cli;
using DedupSharp.Core;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace DedupSharp.Tests;

public class CliArgumentParsingTests
{
    // ---------- defaults ----------

    [Test]
    public async Task ParseArguments_PositionalsBecomePaths_WithDefaults()
    {
        var o = Program.ParseArguments(["a", "b"]);

        await Assert.That(o.Paths).IsEquivalentTo(new[] { "a", "b" });
        await Assert.That(o.Recursive).IsTrue();
        await Assert.That(o.UsePreScan).IsTrue();
        await Assert.That(o.MinSizeBytes).IsEqualTo(1L);
        await Assert.That(o.ExactMode).IsEqualTo(ExactScanMode.BinaryForPairs_HashForGroups);
        await Assert.That(o.HashAlgorithm).IsEqualTo(HashAlgorithmKind.Sha256);
        await Assert.That(o.DryRun).IsTrue();
        await Assert.That(o.DoPlan).IsFalse();
        await Assert.That(o.DoApply).IsFalse();
        await Assert.That(o.AllowDelete).IsFalse();
        await Assert.That(o.AssumeYes).IsFalse();
    }

    // ---------- boolean flags ----------

    [Test]
    public async Task ParseArguments_BooleanFlags()
    {
        var o = Program.ParseArguments(
            ["x", "--no-recursive", "--no-prescan", "--plan", "--apply", "--no-dry-run", "--allow-delete", "--yes"]);

        await Assert.That(o.Recursive).IsFalse();
        await Assert.That(o.UsePreScan).IsFalse();
        await Assert.That(o.DoPlan).IsTrue();
        await Assert.That(o.DoApply).IsTrue();
        await Assert.That(o.DryRun).IsFalse();
        await Assert.That(o.AllowDelete).IsTrue();
        await Assert.That(o.AssumeYes).IsTrue();
    }

    // ---------- valued options ----------

    [Test]
    public async Task ParseArguments_MinSizeAndExtensions()
    {
        var o = Program.ParseArguments(["x", "--min-size", "10K", "--ext", "mp4", "--ext", ".txt"]);

        await Assert.That(o.MinSizeBytes).IsEqualTo(10L * 1024);
        await Assert.That(o.SafeExtensions.Contains(".mp4")).IsTrue();
        await Assert.That(o.SafeExtensions.Contains(".txt")).IsTrue();
    }

    [Test]
    public async Task ParseArguments_ModeHashActionQuarantinePlanFile()
    {
        var o = Program.ParseArguments(
            ["x", "--exact-mode", "hash", "--hash", "blake3", "--action", "delete", "--allow-delete",
             "--quarantine", "/q", "--plan-file", "p.dduplan"]);

        await Assert.That(o.ExactMode).IsEqualTo(ExactScanMode.HashOnly);
        await Assert.That(o.HashAlgorithm).IsEqualTo(HashAlgorithmKind.Blake3);
        await Assert.That(o.ActionKind).IsEqualTo(DupActionKind.Delete);
        await Assert.That(o.QuarantineDirectory).IsEqualTo("/q");
        await Assert.That(o.PlanFile).IsEqualTo("p.dduplan");
    }

    [Test]
    public async Task ParseArguments_IgnoreDirAndFile()
    {
        var o = Program.ParseArguments(["x", "--ignore-dir", ".git", "--ignore-file", "Thumbs.db"]);

        await Assert.That(o.IgnoredDirectoryNames.Contains(".git")).IsTrue();
        await Assert.That(o.IgnoredFileNames.Contains("Thumbs.db")).IsTrue();
    }

    // ---------- errors ----------

    [Test]
    public async Task ParseArguments_UnknownOption_Throws()
    {
        await Assert.That(() => Program.ParseArguments(["x", "--bogus"])).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("--min-size")]
    [Arguments("--ext")]
    [Arguments("--exact-mode")]
    [Arguments("--hash")]
    [Arguments("--action")]
    [Arguments("--quarantine")]
    [Arguments("--plan-file")]
    [Arguments("--ignore-dir")]
    [Arguments("--ignore-file")]
    public async Task ParseArguments_MissingValue_Throws(string flag)
    {
        await Assert.That(() => Program.ParseArguments(["x", flag])).Throws<ArgumentException>();
    }

    [Test]
    public async Task ParseArguments_NoPaths_Throws()
    {
        await Assert.That(() => Program.ParseArguments([])).Throws<ArgumentException>();
    }

    [Test]
    public async Task ParseArguments_ApplyFromPlanFile_NoPaths_Ok()
    {
        var o = Program.ParseArguments(["--apply", "--plan-file", "p.dduplan"]);

        await Assert.That(o.DoApply).IsTrue();
        await Assert.That(o.Paths).IsEmpty();
        await Assert.That(o.PlanFile).IsEqualTo("p.dduplan");
    }

    // ---------- enum parsers ----------

    [Test]
    [Arguments("binary", ExactScanMode.BinaryForPairs_HashForGroups)]
    [Arguments("pairs", ExactScanMode.BinaryForPairs_HashForGroups)]
    [Arguments("hash", ExactScanMode.HashOnly)]
    [Arguments("HASHONLY", ExactScanMode.HashOnly)]
    [Arguments("hash+verify", ExactScanMode.HashWithBinaryVerification)]
    [Arguments("verify", ExactScanMode.HashWithBinaryVerification)]
    public async Task ParseExactMode_Valid(string text, ExactScanMode expected)
    {
        await Assert.That(Program.ParseExactMode(text)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("sha256", HashAlgorithmKind.Sha256)]
    [Arguments("xxh3", HashAlgorithmKind.XxHash3)]
    [Arguments("xxhash128", HashAlgorithmKind.XxHash128)]
    [Arguments("BLAKE3", HashAlgorithmKind.Blake3)]
    [Arguments("b3", HashAlgorithmKind.Blake3)]
    public async Task ParseHashAlgorithm_Valid(string text, HashAlgorithmKind expected)
    {
        await Assert.That(Program.ParseHashAlgorithm(text)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("move", DupActionKind.MoveToQuarantine)]
    [Arguments("quarantine", DupActionKind.MoveToQuarantine)]
    [Arguments("delete", DupActionKind.Delete)]
    [Arguments("del", DupActionKind.Delete)]
    [Arguments("hardlink", DupActionKind.ReplaceWithHardLink)]
    [Arguments("link", DupActionKind.ReplaceWithHardLink)]
    public async Task ParseActionKind_Valid(string text, DupActionKind expected)
    {
        await Assert.That(Program.ParseActionKind(text)).IsEqualTo(expected);
    }

    [Test]
    public async Task ParseEnums_Invalid_Throw()
    {
        await Assert.That(() => Program.ParseExactMode("nope")).Throws<ArgumentException>();
        await Assert.That(() => Program.ParseHashAlgorithm("nope")).Throws<ArgumentException>();
        await Assert.That(() => Program.ParseActionKind("nope")).Throws<ArgumentException>();
    }

    // ---------- size parsing ----------

    [Test]
    [Arguments("100", 100L)]
    [Arguments("10K", 10L * 1024)]
    [Arguments("2M", 2L * 1024 * 1024)]
    [Arguments("1G", 1L * 1024 * 1024 * 1024)]
    [Arguments("0", 0L)]
    public async Task TryParseSize_Valid(string text, long expected)
    {
        var ok = Program.TryParseSize(text, out var bytes);

        await Assert.That(ok).IsTrue();
        await Assert.That(bytes).IsEqualTo(expected);
    }

    [Test]
    [Arguments("abc")]
    [Arguments("")]
    [Arguments("-5")]
    [Arguments("12T")]
    [Arguments("99999999999G")] // overflow
    public async Task TryParseSize_Invalid(string text)
    {
        var ok = Program.TryParseSize(text, out _);

        await Assert.That(ok).IsFalse();
    }
}
