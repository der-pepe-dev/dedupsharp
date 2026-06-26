namespace DedupSharp.Core;

/// <summary>
/// Result summary of applying a batch of duplicate actions.
/// </summary>
public sealed class DuplicateActionApplyResult
{
    public int TotalActions { get; set; }
    public int Applied { get; set; }
    /// <summary>Actions that would have been applied but were not due to DryRun=true.</summary>
    public int DryRunApplied { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public bool DryRun { get; set; }
}
