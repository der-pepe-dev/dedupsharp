using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DedupSharp.Core;

/// <summary>
/// Generates <see cref="DupAction"/> lists from <see cref="DuplicateGroup"/> collections.
/// </summary>
public static class DuplicateActionPlanner
{
    public static List<DupAction> Plan(
        IEnumerable<DuplicateGroup> groups,
        DuplicateActionPlannerOptions options)
    {
        if (groups is null) throw new ArgumentNullException(nameof(groups));
        if (options is null) throw new ArgumentNullException(nameof(options));

        var result = new List<DupAction>();

        foreach (var group in groups)
        {
            if (group.Files.Count <= 1)
                continue;

            var files = group.Files;

            // Choose canonical
            var ordered = options.CanonicalByLexicalPath
                ? files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList()
                : files.ToList();

            var canonical = ordered[0];
            var canonicalInfo = new FileInfo(canonical.Path);

            // Snapshot from disk at plan time so both size and mtime share the same baseline.
            long canonicalSize = canonicalInfo.Exists ? canonicalInfo.Length : canonical.Size;
            DateTime? canonicalMtimeUtc = canonicalInfo.Exists ? canonicalInfo.LastWriteTimeUtc : null;

            // For each other file, create an action with snapshot info
            for (int i = 1; i < ordered.Count; i++)
            {
                var duplicate = ordered[i];

                // Never act on the canonical file itself. A self-pair can arise from
                // overlapping scan inputs; a destructive action here would hit the
                // only copy.
                if (PathsEqual(duplicate.Path, canonical.Path))
                    continue;

                var targetInfo = new FileInfo(duplicate.Path);

                long targetSize = targetInfo.Exists ? targetInfo.Length : duplicate.Size;
                DateTime? targetMtimeUtc = targetInfo.Exists ? targetInfo.LastWriteTimeUtc : null;

                var action = new DupAction
                {
                    Kind = options.ActionKind,
                    CanonicalPath = canonical.Path,
                    TargetPath = duplicate.Path,

                    SizeBytes = group.SizeBytes,

                    CanonicalSnapshotRecorded = true,
                    CanonicalOriginalSizeBytes = canonicalSize,
                    CanonicalOriginalLastWriteTimeUtc = canonicalMtimeUtc,

                    TargetSnapshotRecorded = true,
                    TargetOriginalSizeBytes = targetSize,
                    TargetOriginalLastWriteTimeUtc = targetMtimeUtc
                };

                result.Add(action);
            }
        }

        return result;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
}
