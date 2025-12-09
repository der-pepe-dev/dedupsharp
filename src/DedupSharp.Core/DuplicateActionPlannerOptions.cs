namespace DedupSharp.Core;

/// <summary>
/// Options controlling how duplicate groups are turned into actions.
/// </summary>
public sealed class DuplicateActionPlannerOptions
{
    /// <summary>
    /// The action to generate for each non-canonical file.
    /// </summary>
    public DupActionKind ActionKind { get; init; } = DupActionKind.MoveToQuarantine;

    /// <summary>
    /// When true (default), choose canonical by lexicographically smallest path.
    /// If false, you could later plug in other strategies.
    /// </summary>
    public bool CanonicalByLexicalPath { get; init; } = true;
}
