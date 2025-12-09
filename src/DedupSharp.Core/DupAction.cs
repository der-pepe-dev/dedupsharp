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
    /// Backwards-compat alias for <see cref="Kind"/> that can be used by older code/tests.
    /// </summary>
    public DupActionKind DuplicateKind
    {
        get => Kind;
        init => Kind = value;
    }

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
    /// Canonical file size at planning time (bytes). 0 means "not recorded".
    /// </summary>
    public long CanonicalOriginalSizeBytes { get; init; }

    /// <summary>
    /// Canonical last write time (UTC) at planning time. null means "not recorded".
    /// </summary>
    public DateTime? CanonicalOriginalLastWriteTimeUtc { get; init; }

    /// <summary>
    /// Target file size at planning time (bytes). 0 means "not recorded".
    /// </summary>
    public long TargetOriginalSizeBytes { get; init; }

    /// <summary>
    /// Target last write time (UTC) at planning time. null means "not recorded".
    /// </summary>
    public DateTime? TargetOriginalLastWriteTimeUtc { get; init; }
}
