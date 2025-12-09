using System;

namespace DedupSharp.Core;

/// <summary>
/// Phase of the scan.
/// </summary>
public enum ScanProgressPhase
{
    PreScan = 0,
    SinglePass = 1
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
