using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DedupSharp.Core;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace DedupSharp.Tests;

public class DuplicateActionPlannerTests
{
    [Test]
    public async Task Planner_IgnoresGroupsWithSingleFile()
    {
        var group = new DuplicateGroup(
            10, // sizeBytes
            new[]
            {
                new FileEntry("single.txt", 10)
            });

        var options = new DuplicateActionPlannerOptions
        {
            ActionKind = DupActionKind.MoveToQuarantine,
            CanonicalByLexicalPath = true
        };

        var actions = DuplicateActionPlanner.Plan(new[] { group }, options);

        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task Planner_UsesLexicalCanonical_AndProducesActionsWithSnapshot()
    {
        var files = new List<FileEntry>
        {
            new FileEntry("z.txt", 10),
            new FileEntry("a.txt", 10),
            new FileEntry("b.txt", 10)
        };

        var group = new DuplicateGroup(
            10, // sizeBytes
            files);

        var options = new DuplicateActionPlannerOptions
        {
            ActionKind = DupActionKind.MoveToQuarantine,
            CanonicalByLexicalPath = true
        };

        var actions = DuplicateActionPlanner.Plan(new[] { group }, options);

        // We have 3 files, so 2 actions (everything except canonical).
        await Assert.That(actions.Count).IsEqualTo(2);

        // Canonical should be the lexicographically smallest path: "a.txt".
        const string canonical = "a.txt";
        foreach (var a in actions)
            await Assert.That(a.CanonicalPath).IsEqualTo(canonical);

        // Targets should be the remaining files.
        var targets = actions.Select(a => a.TargetPath).ToHashSet();
        await Assert.That(targets).Contains("b.txt");
        await Assert.That(targets).Contains("z.txt");

        // Group size should be copied to SizeBytes (diagnostic).
        foreach (var a in actions)
            await Assert.That(a.SizeBytes).IsEqualTo(group.SizeBytes);

        // Snapshot sizes should match file sizes (10 bytes in this test).
        foreach (var a in actions)
        {
            await Assert.That(a.CanonicalOriginalSizeBytes).IsEqualTo(10);
            await Assert.That(a.TargetOriginalSizeBytes).IsEqualTo(10);
        }

        // Action kind should be what we configured.
        foreach (var a in actions)
            await Assert.That(a.Kind).IsEqualTo(DupActionKind.MoveToQuarantine);
    }

    [Test]
    public async Task Planner_SkipsSelfPair_WhenDuplicatePathEqualsCanonical()
    {
        // A self-pair (same path twice) must never become a destructive action.
        var group = new DuplicateGroup(
            10,
            new[]
            {
                new FileEntry("same.txt", 10),
                new FileEntry("same.txt", 10)
            });

        var options = new DuplicateActionPlannerOptions
        {
            ActionKind = DupActionKind.Delete,
            CanonicalByLexicalPath = true
        };

        var actions = DuplicateActionPlanner.Plan(new[] { group }, options);

        await Assert.That(actions).IsEmpty();
    }
}
