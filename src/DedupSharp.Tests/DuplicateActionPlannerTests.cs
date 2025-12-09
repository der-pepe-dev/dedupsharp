using System.Collections.Generic;
using System.Linq;
using DedupSharp.Core;
using Xunit;

namespace DedupSharp.Tests;

public class DuplicateActionPlannerTests
{
    [Fact]
    public void Planner_IgnoresGroupsWithSingleFile()
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

        Assert.Empty(actions);
    }

    [Fact]
    public void Planner_UsesLexicalCanonical_AndProducesActionsWithSnapshot()
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
        Assert.Equal(2, actions.Count);

        // Canonical should be the lexicographically smallest path: "a.txt".
        const string canonical = "a.txt";
        Assert.All(actions, a => Assert.Equal(canonical, a.CanonicalPath));

        // Targets should be the remaining files.
        var targets = actions.Select(a => a.TargetPath).ToHashSet();
        Assert.Contains("b.txt", targets);
        Assert.Contains("z.txt", targets);

        // Group size should be copied to SizeBytes (diagnostic).
        Assert.All(actions, a => Assert.Equal(group.SizeBytes, a.SizeBytes));

        // Snapshot sizes should match file sizes (10 bytes in this test).
        Assert.All(actions, a =>
        {
            Assert.Equal(10, a.CanonicalOriginalSizeBytes);
            Assert.Equal(10, a.TargetOriginalSizeBytes);
        });

        // Action kind should be what we configured.
        Assert.All(actions, a => Assert.Equal(DupActionKind.MoveToQuarantine, a.Kind));
    }
}
