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

            DateTime? canonicalMtimeUtc = canonicalInfo.Exists
                ? canonicalInfo.LastWriteTimeUtc
                : null;

            long canonicalSize = canonical.Size;

            // For each other file, create an action with snapshot info
            for (int i = 1; i < ordered.Count; i++)
            {
                var duplicate = ordered[i];
                var targetInfo = new FileInfo(duplicate.Path);

                DateTime? targetMtimeUtc = targetInfo.Exists
                    ? targetInfo.LastWriteTimeUtc
                    : null;

                var action = new DupAction
                {
                    Kind = options.ActionKind,
                    CanonicalPath = canonical.Path,
                    TargetPath = duplicate.Path,

                    // Group size for tests/diagnostics
                    SizeBytes = group.SizeBytes,

                    CanonicalOriginalSizeBytes = canonicalSize,
                    CanonicalOriginalLastWriteTimeUtc = canonicalMtimeUtc,

                    TargetOriginalSizeBytes = duplicate.Size,
                    TargetOriginalLastWriteTimeUtc = targetMtimeUtc
                };

                result.Add(action);
            }
        }

        return result;
    }
}
