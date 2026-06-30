using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using DedupSharp.Core;
using DedupSharp.Core.Exact;
using DedupSharp.Core.Media;

[assembly: InternalsVisibleTo("DedupSharp.Tests")]

namespace DedupSharp.Cli;

// Simple CLI for DedupSharp:
//  - Scan for exact duplicates (using ExactDuplicateScanner)
//  - Optionally write a plan file (.dduplan)
//  - Optionally apply a plan (immediately or from file)

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var opts = ParseArguments(args);

        var paths = opts.Paths;
        bool recursive = opts.Recursive;
        bool usePreScan = opts.UsePreScan;
        long minSizeBytes = opts.MinSizeBytes;
        var safeExtensions = opts.SafeExtensions;
        var ignoredDirs = opts.IgnoredDirectoryNames;
        var ignoredFiles = opts.IgnoredFileNames;
        ExactScanMode exactMode = opts.ExactMode;
        HashAlgorithmKind hashAlgorithm = opts.HashAlgorithm;

        bool doPlan = opts.DoPlan;
        bool doApply = opts.DoApply;
        bool dryRun = opts.DryRun;
        bool allowDelete = opts.AllowDelete;
        string? planFile = opts.PlanFile;
        string? quarantineDir = opts.QuarantineDirectory;
        DupActionKind actionKind = opts.ActionKind;
        bool assumeYes = opts.AssumeYes;
        bool media = opts.Media;

        if (doApply && actionKind == DupActionKind.Delete && !allowDelete)
        {
            Console.WriteLine("WARNING: --action delete specified without --allow-delete. Delete actions will fail.");
        }

        // Ctrl+C cancels the scan/apply gracefully instead of killing the process.
        // Set up before any apply path (including loading a saved plan below).
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // don't terminate immediately; let the token unwind
            cts.Cancel();
            Console.Error.WriteLine("Cancelling... (Ctrl+C)");
        };

        // If we have a plan file and only apply was requested, load and apply.
        if (doApply && !string.IsNullOrEmpty(planFile) && paths.Count == 0)
        {
            var plan = DuplicatePlanFile.Load(planFile!);
            Console.WriteLine($"Loaded plan with {plan.Actions.Count} actions, created {plan.CreatedUtc:O}.");

            var applyOptions = new DuplicateActionApplyOptions
            {
                DryRun = dryRun,
                QuarantineDirectory = quarantineDir,
                AllowDelete = allowDelete
            };

            if (!assumeYes && !applyOptions.DryRun)
            {
                if (Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Refusing to apply without confirmation on non-interactive input. Pass --yes to proceed.");
                    return 1;
                }

                Console.Write($"About to apply {plan.Actions.Count} actions. Continue? [y/N]: ");
                var key = Console.ReadKey();
                Console.WriteLine();
                if (key.KeyChar is not ('y' or 'Y'))
                {
                    Console.WriteLine("Aborted by user.");
                    return 0;
                }
            }

            DuplicateActionApplyResult result;
            try
            {
                result = DuplicateActionApplier.Apply(plan.Actions, applyOptions, s => Console.WriteLine(s), cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Apply cancelled.");
                return 130;
            }

            Console.WriteLine(
                $"Apply result: Total={result.TotalActions}, Applied={result.Applied}, Skipped={result.Skipped}, Failed={result.Failed}, DryRun={result.DryRun}");
            return 0;
        }

        // Otherwise: perform a scan, optionally write a plan, optionally apply it immediately.
        IDuplicateScanner scanner = media
            ? new MediaImageScanner()
            : new ExactDuplicateScanner();

        var scanOptions = new ScanOptions
        {
            Paths = paths,
            Recursive = recursive,
            UsePreScan = usePreScan,
            MinFileSizeBytes = minSizeBytes,
            ExactMode = exactMode,
            HashAlgorithm = hashAlgorithm,
            PerceptualHash = opts.PerceptualHash,
            HammingThreshold = opts.HammingThreshold,
            SafeExtensions = safeExtensions,
            IgnoredDirectoryNames = ignoredDirs,
            IgnoredFileNames = ignoredFiles,
            ProgressInterval = 1000,
            CancellationToken = cts.Token,
            Progress = p =>
            {
                if (!p.IsPhaseCompleted)
                    return;

                Console.WriteLine($"[{p.Phase}] Files={p.FilesScanned}, Bytes={p.BytesScanned}");
            }
        };

        // In media mode, --ext (if given) restricts which image extensions are scanned.
        if (media && safeExtensions.Count > 0)
            scanOptions.ImageExtensions = safeExtensions;

        Console.WriteLine("Scanning...");
        List<DuplicateGroup> groups;
        try
        {
            groups = scanner.Scan(scanOptions).ToList();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Scan cancelled.");
            return 130; // 128 + SIGINT
        }

        if (groups.Count == 0)
        {
            Console.WriteLine("No duplicates found.");
            return 0;
        }

        Console.WriteLine($"Found {groups.Count} duplicate group(s):");
        foreach (var group in groups)
        {
            Console.WriteLine();
            Console.WriteLine($"Group [{group.Kind}] - Size: {group.SizeBytes} bytes, Files: {group.Files.Count}");
            foreach (var f in group.Files)
            {
                Console.WriteLine($"  {f.Path}");
            }
        }

        // Build actions
        var plannerOptions = new DuplicateActionPlannerOptions
        {
            ActionKind = actionKind
        };

        var actions = DuplicateActionPlanner.Plan(groups, plannerOptions);
        Console.WriteLine();
        Console.WriteLine($"Planned {actions.Count} action(s).");

        // If requested, write plan file
        if (doPlan)
        {
            var effectivePlanFile = planFile ?? "dedup.plan.dduplan";

            var plan = new DuplicatePlan
            {
                CreatedUtc = DateTime.UtcNow,
                Metadata = new DuplicatePlanMetadata
                {
                    Paths = paths,
                    Recursive = recursive,
                    UsePreScan = usePreScan,
                    MinSizeBytes = minSizeBytes,
                    ExactMode = exactMode,
                    ActionKind = actionKind,
                    MachineName = Environment.MachineName,
                    OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription
                },
                Actions = actions
            };

            DuplicatePlanFile.Save(effectivePlanFile, plan);
            Console.WriteLine($"Plan written to: {effectivePlanFile}");
        }

        if (!doApply)
            return 0;

        var applyOpts = new DuplicateActionApplyOptions
        {
            DryRun = dryRun,
            QuarantineDirectory = quarantineDir,
            AllowDelete = allowDelete
        };

        if (!assumeYes && !applyOpts.DryRun)
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("Refusing to apply without confirmation on non-interactive input. Pass --yes to proceed.");
                return 1;
            }

            Console.Write($"About to apply {actions.Count} actions. Continue? [y/N]: ");
            var key = Console.ReadKey();
            Console.WriteLine();
            if (key.KeyChar is not ('y' or 'Y'))
            {
                Console.WriteLine("Aborted by user.");
                return 0;
            }
        }

        DuplicateActionApplyResult res;
        try
        {
            res = DuplicateActionApplier.Apply(actions, applyOpts, s => Console.WriteLine(s), cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Apply cancelled.");
            return 130;
        }

        Console.WriteLine(
            $"Apply result: Total={res.TotalActions}, Applied={res.Applied}, Skipped={res.Skipped}, Failed={res.Failed}, DryRun={res.DryRun}");

        return 0;
    }

    internal static CliOptions ParseArguments(string[] args)
    {
        var paths = new List<string>();

        bool recursive = true;
        bool usePreScan = true;
        long minSizeBytes = 1;
        var safeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignoredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ExactScanMode exactMode = ExactScanMode.BinaryForPairs_HashForGroups;
        HashAlgorithmKind hashAlgorithm = HashAlgorithmKind.Sha256;

        bool doPlan = false;
        bool doApply = false;
        bool dryRun = true;
        bool allowDelete = false;
        string? planFile = null;
        string? quarantineDir = null;
        DupActionKind actionKind = DupActionKind.MoveToQuarantine;
        bool assumeYes = false;

        bool media = false;
        PerceptualHashKind perceptualHash = PerceptualHashKind.DHash;
        int hammingThreshold = 10;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                switch (arg)
                {
                    case "--no-prescan":
                        usePreScan = false;
                        break;

                    case "--recursive":
                        recursive = true;
                        break;

                    case "--no-recursive":
                        recursive = false;
                        break;

                    case "--min-size":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--min-size requires a value.");
                        i++;
                        if (!TryParseSize(args[i], out minSizeBytes))
                            throw new ArgumentException($"Invalid size value: {args[i]}");
                        break;

                    case "--ext":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--ext requires a value.");
                        i++;
                        var ext = args[i];
                        if (!ext.StartsWith(".", StringComparison.Ordinal))
                            ext = "." + ext;
                        safeExtensions.Add(ext);
                        break;

                    case "--ignore-dir":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--ignore-dir requires a value.");
                        i++;
                        ignoredDirs.Add(args[i]);
                        break;

                    case "--ignore-file":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--ignore-file requires a value.");
                        i++;
                        ignoredFiles.Add(args[i]);
                        break;

                    case "--exact-mode":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--exact-mode requires a value.");
                        i++;
                        exactMode = ParseExactMode(args[i]);
                        break;

                    case "--hash":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--hash requires a value.");
                        i++;
                        hashAlgorithm = ParseHashAlgorithm(args[i]);
                        break;

                    case "--media":
                        media = true;
                        break;

                    case "--perceptual-hash":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--perceptual-hash requires a value.");
                        i++;
                        perceptualHash = ParsePerceptualHash(args[i]);
                        break;

                    case "--hamming":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--hamming requires a value.");
                        i++;
                        if (!int.TryParse(args[i], out hammingThreshold) || hammingThreshold is < 0 or > 64)
                            throw new ArgumentException($"--hamming must be an integer 0-64: {args[i]}");
                        break;

                    case "--plan":
                        doPlan = true;
                        break;

                    case "--apply":
                        doApply = true;
                        break;

                    case "--plan-file":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--plan-file requires a value.");
                        i++;
                        planFile = args[i];
                        break;

                    case "--dry-run":
                        dryRun = true;
                        break;

                    case "--no-dry-run":
                        dryRun = false;
                        break;

                    case "--action":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--action requires a value.");
                        i++;
                        actionKind = ParseActionKind(args[i]);
                        break;

                    case "--quarantine":
                        if (i + 1 >= args.Length)
                            throw new ArgumentException("--quarantine requires a value.");
                        i++;
                        quarantineDir = args[i];
                        break;

                    case "--allow-delete":
                        allowDelete = true;
                        break;

                    case "--yes":
                        assumeYes = true;
                        break;

                    default:
                        throw new ArgumentException($"Unknown option: {arg}");
                }
            }
            else
            {
                paths.Add(arg);
            }
        }

        if (paths.Count == 0 && string.IsNullOrEmpty(planFile) && !doApply)
            throw new ArgumentException("No paths specified.");

        if (doApply && string.IsNullOrEmpty(planFile) && paths.Count == 0)
            throw new ArgumentException("Apply requires either --plan-file or scan paths to build a plan from.");

        return new CliOptions
        {
            Paths = paths,
            Recursive = recursive,
            UsePreScan = usePreScan,
            MinSizeBytes = minSizeBytes,
            SafeExtensions = safeExtensions,
            IgnoredDirectoryNames = ignoredDirs,
            IgnoredFileNames = ignoredFiles,
            ExactMode = exactMode,
            HashAlgorithm = hashAlgorithm,
            DoPlan = doPlan,
            DoApply = doApply,
            DryRun = dryRun,
            AllowDelete = allowDelete,
            PlanFile = planFile,
            QuarantineDirectory = quarantineDir,
            ActionKind = actionKind,
            AssumeYes = assumeYes,
            Media = media,
            PerceptualHash = perceptualHash,
            HammingThreshold = hammingThreshold
        };
    }

    internal static PerceptualHashKind ParsePerceptualHash(string value) =>
        value.ToLowerInvariant() switch
        {
            "ahash" or "average" => PerceptualHashKind.AHash,
            "dhash" or "difference" => PerceptualHashKind.DHash,
            "phash" or "perceptual" => PerceptualHashKind.PHash,
            _ => throw new ArgumentException($"Unknown perceptual hash: {value}")
        };

    internal static ExactScanMode ParseExactMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "binary" or "binaryforpairs" or "pairs" => ExactScanMode.BinaryForPairs_HashForGroups,
            "hash" or "hashonly" => ExactScanMode.HashOnly,
            "hash+verify" or "hashverify" or "verify" => ExactScanMode.HashWithBinaryVerification,
            _ => throw new ArgumentException($"Unknown exact mode: {value}")
        };

    internal static HashAlgorithmKind ParseHashAlgorithm(string value) =>
        value.ToLowerInvariant() switch
        {
            "sha256" or "sha-256" => HashAlgorithmKind.Sha256,
            "xxhash3" or "xxh3" => HashAlgorithmKind.XxHash3,
            "xxhash128" or "xxh128" => HashAlgorithmKind.XxHash128,
            "blake3" or "b3" => HashAlgorithmKind.Blake3,
            _ => throw new ArgumentException($"Unknown hash algorithm: {value}")
        };

    internal static DupActionKind ParseActionKind(string value) =>
        value.ToLowerInvariant() switch
        {
            "move" or "quarantine" => DupActionKind.MoveToQuarantine,
            "delete" or "del" => DupActionKind.Delete,
            "hardlink" or "link" => DupActionKind.ReplaceWithHardLink,
            _ => throw new ArgumentException($"Unknown action: {value}")
        };

    internal static bool TryParseSize(string text, out long bytes)
    {
        // Supports plain bytes, or suffixes: K, M, G (binary, 1024-based).
        // e.g. 100K, 10M, 1G
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        char last = text[^1];

        long multiplier = 1;
        string numberPart = text;

        if (char.IsLetter(last))
        {
            numberPart = text[..^1];
            switch (char.ToUpperInvariant(last))
            {
                case 'K':
                    multiplier = 1024L;
                    break;
                case 'M':
                    multiplier = 1024L * 1024L;
                    break;
                case 'G':
                    multiplier = 1024L * 1024L * 1024L;
                    break;
                default:
                    return false;
            }
        }

        if (!long.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;

        if (value < 0)
            return false;

        try
        {
            bytes = checked(value * multiplier);
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("DedupSharp CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dedupsharp [options] <path1> [path2 ...]");
        Console.WriteLine();
        Console.WriteLine("Scan / filter options:");
        Console.WriteLine("  --recursive                Recurse into subdirectories (default)");
        Console.WriteLine("  --no-recursive             Do not recurse into subdirectories");
        Console.WriteLine("  --no-prescan               Disable pre-scan size pass");
        Console.WriteLine("  --min-size <N[K|M|G]>      Minimum file size to consider (default 1 byte)");
        Console.WriteLine("  --ext <ext>                Only include this extension (e.g. .mp4 or mp4). Can be repeated.");
        Console.WriteLine("  --ignore-dir <name>        Skip directories with this name (e.g. .zfs). Can be repeated.");
        Console.WriteLine("  --ignore-file <name>       Skip files with this name (e.g. Thumbs.db). Can be repeated.");
        Console.WriteLine();
        Console.WriteLine("Exact comparison:");
        Console.WriteLine("  --exact-mode <mode>        Mode: binary, hash, hash+verify");
        Console.WriteLine("                             binary       = binary compare for pairs, hash for groups (default)");
        Console.WriteLine("                             hash         = hash-only");
        Console.WriteLine("                             hash+verify  = hash then binary-verify per group");
        Console.WriteLine("  --hash <algorithm>         sha256 (default), xxhash3, xxhash128, blake3.");
        Console.WriteLine("                             Non-crypto hashes (xxhash*) are always binary-verified;");
        Console.WriteLine("                             blake3 is cryptographic and trusted like sha256.");
        Console.WriteLine();
        Console.WriteLine("Media (perceptual image) mode:");
        Console.WriteLine("  --media                    Find visually-similar images instead of exact duplicates");
        Console.WriteLine("  --perceptual-hash <algo>   ahash, dhash (default), phash");
        Console.WriteLine("  --hamming <N>              Max Hamming distance 0-64 for a match (default 10)");
        Console.WriteLine();
        Console.WriteLine("Plan / apply:");
        Console.WriteLine("  --plan                     Write a plan file (scan-only by default writes nothing)");
        Console.WriteLine("  --apply                    Apply actions (may be combined with --plan)");
        Console.WriteLine("  --plan-file <path>         Plan file to read/write (default: dedup.plan.dduplan)");
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  --action <kind>            move (default), delete, hardlink");
        Console.WriteLine("  --quarantine <dir>         Quarantine directory for move action");
        Console.WriteLine("  --allow-delete             Allow delete actions");
        Console.WriteLine();
        Console.WriteLine("Safety:");
        Console.WriteLine("  --dry-run                  Do not modify the filesystem (default)");
        Console.WriteLine("  --no-dry-run               Actually perform actions");
        Console.WriteLine("  --yes                      Do not prompt for confirmation");
        Console.WriteLine();
    }
}

/// <summary>
/// Parsed CLI options (pure result of <see cref="Program.ParseArguments"/>, no I/O).
/// </summary>
internal sealed class CliOptions
{
    public List<string> Paths { get; init; } = new();
    public bool Recursive { get; init; } = true;
    public bool UsePreScan { get; init; } = true;
    public long MinSizeBytes { get; init; } = 1;
    public HashSet<string> SafeExtensions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IgnoredDirectoryNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IgnoredFileNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public ExactScanMode ExactMode { get; init; } = ExactScanMode.BinaryForPairs_HashForGroups;
    public HashAlgorithmKind HashAlgorithm { get; init; } = HashAlgorithmKind.Sha256;
    public bool DoPlan { get; init; }
    public bool DoApply { get; init; }
    public bool DryRun { get; init; } = true;
    public bool AllowDelete { get; init; }
    public string? PlanFile { get; init; }
    public string? QuarantineDirectory { get; init; }
    public DupActionKind ActionKind { get; init; } = DupActionKind.MoveToQuarantine;
    public bool AssumeYes { get; init; }

    // Media (perceptual image) mode.
    public bool Media { get; init; }
    public PerceptualHashKind PerceptualHash { get; init; } = PerceptualHashKind.DHash;
    public int HammingThreshold { get; init; } = 10;
}
