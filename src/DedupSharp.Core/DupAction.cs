using System;

namespace DedupSharp.Core;

/// <summary>
/// A single planned action for handling a duplicate file.
/// </summary>
public sealed class DupAction
{
    /// <summary>
    /// What to do with the target (move/delete/hardlink).
    /// This is the primary field used by the core and CLI.
    /// </summary>
    public DupActionKind Kind { get; init; }

    /// <summary>
    /// The file we keep in the group (canonical).
    /// </summary>
    public string CanonicalPath { get; init; } = string.Empty;

    /// <summary>
    /// The duplicate file on which this action will operate.
    /// </summary>
    public string TargetPath { get; init; } = string.Empty;

    /// <summary>
    /// Size of the duplicate group (bytes). Used for tests/diagnostics.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Whether a canonical snapshot (size/mtime) was recorded at planning time.
    /// Drift detection for the canonical file only runs when this is true, so a
    /// 0-byte file is distinguished from "no snapshot".
    /// </summary>
    public bool CanonicalSnapshotRecorded { get; init; }

    /// <summary>
    /// Canonical file size at planning time (bytes). Meaningful only when
    /// <see cref="CanonicalSnapshotRecorded"/> is true.
    /// </summary>
    public long CanonicalOriginalSizeBytes { get; init; }

    /// <summary>
    /// Canonical last write time (UTC) at planning time. null means "not recorded".
    /// </summary>
    public DateTime? CanonicalOriginalLastWriteTimeUtc { get; init; }

    /// <summary>
    /// Whether a target snapshot (size/mtime) was recorded at planning time.
    /// Drift detection for the target only runs when this is true, so a 0-byte
    /// file is distinguished from "no snapshot".
    /// </summary>
    public bool TargetSnapshotRecorded { get; init; }

    /// <summary>
    /// Target file size at planning time (bytes). Meaningful only when
    /// <see cref="TargetSnapshotRecorded"/> is true.
    /// </summary>
    public long TargetOriginalSizeBytes { get; init; }

    /// <summary>
    /// Target last write time (UTC) at planning time. null means "not recorded".
    /// </summary>
    public DateTime? TargetOriginalLastWriteTimeUtc { get; init; }
}
