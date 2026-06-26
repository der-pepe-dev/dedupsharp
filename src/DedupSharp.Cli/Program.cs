using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using DedupSharp.Core;
using DedupSharp.Core.Exact;

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

        // Simple manual parsing
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
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--min-size requires a value.");

                            i++;
                            if (!TryParseSize(args[i], out minSizeBytes))
                                throw new ArgumentException($"Invalid size value: {args[i]}");
                            break;
                        }

                    case "--ext":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--ext requires a value.");

                            i++;
                            var ext = args[i];
                            if (!ext.StartsWith(".", StringComparison.Ordinal))
                                ext = "." + ext;
                            safeExtensions.Add(ext);
                            break;
                        }

                    case "--ignore-dir":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--ignore-dir requires a value.");
                            i++;
                            ignoredDirs.Add(args[i]);
                            break;
                        }

                    case "--ignore-file":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--ignore-file requires a value.");
                            i++;
                            ignoredFiles.Add(args[i]);
                            break;
                        }

                    case "--exact-mode":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--exact-mode requires a value.");

                            i++;
                            var modeStr = args[i].ToLowerInvariant();
                            exactMode = modeStr switch
                            {
                                "binary" or "binaryforpairs" or "pairs" =>
                                    ExactScanMode.BinaryForPairs_HashForGroups,
                                "hash" or "hashonly" =>
                                    ExactScanMode.HashOnly,
                                "hash+verify" or "hashverify" or "verify" =>
                                    ExactScanMode.HashWithBinaryVerification,
                                _ => throw new ArgumentException($"Unknown exact mode: {args[i]}")
                            };
                            break;
                        }

                    case "--hash":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--hash requires a value.");

                            i++;
                            var hashStr = args[i].ToLowerInvariant();
                            hashAlgorithm = hashStr switch
                            {
                                "sha256" or "sha-256" => HashAlgorithmKind.Sha256,
                                "xxhash3" or "xxh3" => HashAlgorithmKind.XxHash3,
                                "xxhash128" or "xxh128" => HashAlgorithmKind.XxHash128,
                                "blake3" or "b3" => HashAlgorithmKind.Blake3,
                                _ => throw new ArgumentException($"Unknown hash algorithm: {args[i]}")
                            };
                            break;
                        }

                    case "--plan":
                        doPlan = true;
                        break;

                    case "--apply":
                        doApply = true;
                        break;

                    case "--plan-file":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--plan-file requires a value.");
                            i++;
                            planFile = args[i];
                            break;
                        }

                    case "--dry-run":
                        dryRun = true;
                        break;

                    case "--no-dry-run":
                        dryRun = false;
                        break;

                    case "--action":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--action requires a value.");

                            i++;
                            var actionStr = args[i].ToLowerInvariant();
                            actionKind = actionStr switch
                            {
                                "move" or "quarantine" =>
                                    DupActionKind.MoveToQuarantine,
                                "delete" or "del" =>
                                    DupActionKind.Delete,
                                "hardlink" or "link" =>
                                    DupActionKind.ReplaceWithHardLink,
                                _ => throw new ArgumentException($"Unknown action: {args[i]}")
                            };
                            break;
                        }

                    case "--quarantine":
                        {
                            if (i + 1 >= args.Length)
                                throw new ArgumentException("--quarantine requires a value.");
                            i++;
                            quarantineDir = args[i];
                            break;
                        }

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

        // Default is scan-only (report duplicates). A plan file is written only when
        // --plan is given explicitly, so a plain scan has no filesystem side effects.

        if (doApply && string.IsNullOrEmpty(planFile) && paths.Count == 0)
            throw new ArgumentException("Apply requires either --plan-file or scan paths to build a plan from.");

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
        var scanner = new ExactDuplicateScanner();

        var scanOptions = new ScanOptions
        {
            Paths = paths,
            Recursive = recursive,
            UsePreScan = usePreScan,
            MinFileSizeBytes = minSizeBytes,
            ExactMode = exactMode,
            HashAlgorithm = hashAlgorithm,
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
            Console.WriteLine($"Group - Size: {group.SizeBytes} bytes, Files: {group.Files.Count}");
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

    private static bool TryParseSize(string text, out long bytes)
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
