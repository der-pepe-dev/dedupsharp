namespace DedupSharp.Core;

/// <summary>
/// Options controlling how duplicate actions are applied to the filesystem.
/// </summary>
public sealed class DuplicateActionApplyOptions
{
    /// <summary>
    /// When true, no changes are made and all actions are logged as "DRY".
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Directory where duplicates will be moved when using MoveToQuarantine.
    /// Required when any MoveToQuarantine actions are applied.
    /// </summary>
    public string? QuarantineDirectory { get; set; }

    /// <summary>
    /// Whether Delete actions are allowed. When false, Delete will throw.
    /// </summary>
    public bool AllowDelete { get; set; } = false;
}
