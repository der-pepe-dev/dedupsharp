using System.Collections.Generic;

namespace DedupSharp.Core;

/// <summary>
/// Abstraction for a duplicate scanner implementation.
/// </summary>
public interface IDuplicateScanner
{
    /// <summary>
    /// Performs a scan and returns duplicate groups.
    /// </summary>
    IEnumerable<DuplicateGroup> Scan(ScanOptions options);
}
