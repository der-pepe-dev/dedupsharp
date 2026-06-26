using System;

namespace DedupSharp.Core;

/// <summary>
/// Phase of the scan.
/// </summary>
public enum ScanProgressPhase
{
    PreScan = 0,
    /// <summary>Second pass in pre-scan mode: builds detailed entries for candidate sizes only.</summary>
    CandidateScan = 1,
    /// <summary>Single-pass mode (no pre-scan): one pass over all files.</summary>
    SinglePass = 2
}

/// <summary>
/// Represents a progress update during scanning.
/// </summary>
public readonly struct ScanProgress
{
    public ScanProgressPhase Phase { get; }
    public long FilesScanned { get; }
    public long BytesScanned { get; }
    public bool IsPhaseCompleted { get; }

    public ScanProgress(ScanProgressPhase phase, long filesScanned, long bytesScanned, bool isPhaseCompleted)
    {
        Phase = phase;
        FilesScanned = filesScanned;
        BytesScanned = bytesScanned;
        IsPhaseCompleted = isPhaseCompleted;
    }
}
