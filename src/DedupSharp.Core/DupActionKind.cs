namespace DedupSharp.Core;

/// <summary>
/// Kind of action to take for a duplicate file.
/// </summary>
public enum DupActionKind
{
    MoveToQuarantine = 0,
    Delete = 1,
    ReplaceWithHardLink = 2
}
