using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DedupSharp.Core;

/// <summary>
/// Represents a serialized deduplication plan: metadata + actions.
/// </summary>
public sealed class DuplicatePlan
{
    public int Version { get; set; } = 1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DuplicatePlanMetadata Metadata { get; set; } = new();
    public List<DupAction> Actions { get; set; } = new();
}

/// <summary>
/// Metadata describing how the plan was generated (scan options, environment).
/// </summary>
public sealed class DuplicatePlanMetadata
{
    public List<string> Paths { get; set; } = new();
    public bool Recursive { get; set; }
    public bool UsePreScan { get; set; }
    public long MinSizeBytes { get; set; }
    public ExactScanMode ExactMode { get; set; }
    public DupActionKind ActionKind { get; set; }
    public string? MachineName { get; set; }
    public string? OsDescription { get; set; }
}

/// <summary>
/// Helper for saving/loading <see cref="DuplicatePlan"/> to/from disk.
/// </summary>
public static class DuplicatePlanFile
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, DuplicatePlan plan)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(plan, s_jsonOptions);
        File.WriteAllText(fullPath, json);
    }

    public static DuplicatePlan Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Plan file not found.", fullPath);

        var json = File.ReadAllText(fullPath);
        var plan = JsonSerializer.Deserialize<DuplicatePlan>(json, s_jsonOptions);
        if (plan is null)
            throw new InvalidOperationException($"Failed to deserialize plan file '{fullPath}'.");

        return plan;
    }
}
