using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DedupSharp.Core;

/// <summary>
/// Applies duplicate actions to the filesystem (move to quarantine, delete, hardlink).
/// All potentially destructive behaviour is controlled by <see cref="DuplicateActionApplyOptions"/>.
/// Includes drift protection based on size + last-write snapshots stored in <see cref="DupAction"/>.
/// </summary>
public static class DuplicateActionApplier
{
    public static DuplicateActionApplyResult Apply(
        IEnumerable<DupAction> actions,
        DuplicateActionApplyOptions options,
        Action<string>? log = null)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (options is null) throw new ArgumentNullException(nameof(options));

        int total = 0;
        int applied = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var action in actions)
        {
            total++;

            try
            {
                switch (action.Kind)
                {
                    case DupActionKind.MoveToQuarantine:
                        ApplyMoveToQuarantine(action, options, ref applied, ref skipped, log);
                        break;

                    case DupActionKind.Delete:
                        ApplyDelete(action, options, ref applied, ref skipped, log);
                        break;

                    case DupActionKind.ReplaceWithHardLink:
                        ApplyHardLink(action, options, ref applied, ref skipped, log);
                        break;

                    default:
                        skipped++;
                        log?.Invoke($"SKIP  Unknown action kind '{action.Kind}' for {action.TargetPath}");
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                log?.Invoke($"ERROR {action.Kind} on {action.TargetPath}: {ex.Message}");
            }
        }

        return new DuplicateActionApplyResult
        {
            TotalActions = total,
            Applied = applied,
            Skipped = skipped,
            Failed = failed,
            DryRun = options.DryRun
        };
    }

    // ----------------- MoveToQuarantine -----------------

    private static void ApplyMoveToQuarantine(
        DupAction action,
        DuplicateActionApplyOptions options,
        ref int applied,
        ref int skipped,
        Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(options.QuarantineDirectory))
            throw new InvalidOperationException("QuarantineDirectory must be set for MoveToQuarantine.");

        if (HasDrifted(action, out var driftReason))
        {
            skipped++;
            log?.Invoke($"SKIP  MoveToQuarantine (drift): {driftReason}");
            return;
        }

        var targetPath = action.TargetPath;
        if (!File.Exists(targetPath))
        {
            skipped++;
            log?.Invoke($"SKIP  MoveToQuarantine (missing): {targetPath}");
            return;
        }

        var quarantineDir = Path.GetFullPath(options.QuarantineDirectory);
        var fileName = Path.GetFileName(targetPath);
        var destDir = quarantineDir;
        var destPath = Path.Combine(destDir, fileName);

        if (!options.DryRun)
        {
            Directory.CreateDirectory(destDir);
            destPath = GetUniquePath(destPath);
        }
        else
        {
            // For dry-run, also show the unique path we would pick
            destPath = GetUniquePath(destPath);
        }

        if (options.DryRun)
        {
            skipped++;
            log?.Invoke($"DRY  MoveToQuarantine: {targetPath} -> {destPath}");
            return;
        }

        File.Move(targetPath, destPath);
        applied++;
        log?.Invoke($"APPLY MoveToQuarantine: {targetPath} -> {destPath}");
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        int counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name}._dup{counter}{ext}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }

    // ----------------- Delete -----------------

    private static void ApplyDelete(
        DupAction action,
        DuplicateActionApplyOptions options,
        ref int applied,
        ref int skipped,
        Action<string>? log)
    {
        if (!options.AllowDelete)
            throw new InvalidOperationException(
                "Delete is not allowed. Set AllowDelete=true in DuplicateActionApplyOptions.");

        if (HasDrifted(action, out var driftReason))
        {
            skipped++;
            log?.Invoke($"SKIP  Delete (drift): {driftReason}");
            return;
        }

        var targetPath = action.TargetPath;
        if (!File.Exists(targetPath))
        {
            skipped++;
            log?.Invoke($"SKIP  Delete (missing): {targetPath}");
            return;
        }

        if (options.DryRun)
        {
            skipped++;
            log?.Invoke($"DRY  Delete: {targetPath}");
            return;
        }

        File.Delete(targetPath);
        applied++;
        log?.Invoke($"APPLY Delete: {targetPath}");
    }

    // ----------------- ReplaceWithHardLink -----------------

    private static void ApplyHardLink(
        DupAction action,
        DuplicateActionApplyOptions options,
        ref int applied,
        ref int skipped,
        Action<string>? log)
    {
        if (HasDrifted(action, out var driftReason))
        {
            skipped++;
            log?.Invoke($"SKIP  HardLink (drift): {driftReason}");
            return;
        }

        var canonical = action.CanonicalPath;
        var target = action.TargetPath;

        if (!File.Exists(canonical))
        {
            skipped++;
            log?.Invoke($"SKIP  HardLink (canonical missing): {canonical}");
            return;
        }

        if (!File.Exists(target))
        {
            skipped++;
            log?.Invoke($"SKIP  HardLink (target missing): {target}");
            return;
        }

        if (options.DryRun)
        {
            skipped++;
            log?.Invoke($"DRY  HardLink: {target} -> {canonical}");
            return;
        }

        // Delete the duplicate file and create a hard link at the same path pointing to canonical.
        File.Delete(target);

        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkWindows(target, canonical, IntPtr.Zero))
            {
                var err = Marshal.GetLastWin32Error();
                throw new IOException($"CreateHardLink failed with Win32 error {err} for {target}");
            }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // link(canonical, target)
            int result = LinkUnix(canonical, target);
            if (result != 0)
            {
                var err = Marshal.GetLastWin32Error();
                throw new IOException($"link() failed with errno {err} for {target}");
            }
        }
        else
        {
            throw new PlatformNotSupportedException("Hardlink creation is not supported on this platform.");
        }

        applied++;
        log?.Invoke($"APPLY HardLink: {target} -> {canonical}");
    }

    // ----------------- Drift detection -----------------

    /// <summary>
    /// Checks whether either canonical or target has changed since planning (size/mtime).
    /// If no snapshot fields are populated, returns false (drift check disabled).
    /// </summary>
    private static bool HasDrifted(DupAction action, out string reason)
    {
        reason = string.Empty;

        bool hasSnapshot =
            action.CanonicalOriginalSizeBytes > 0 ||
            action.CanonicalOriginalLastWriteTimeUtc.HasValue ||
            action.TargetOriginalSizeBytes > 0 ||
            action.TargetOriginalLastWriteTimeUtc.HasValue;

        // Old plan files (no snapshot info): no drift protection.
        if (!hasSnapshot)
            return false;

        // Target checks
        if (action.TargetOriginalSizeBytes > 0 || action.TargetOriginalLastWriteTimeUtc.HasValue)
        {
            var ti = new FileInfo(action.TargetPath);
            if (!ti.Exists)
            {
                reason = $"target missing since plan was created: {action.TargetPath}";
                return true;
            }

            if (action.TargetOriginalSizeBytes > 0 && ti.Length != action.TargetOriginalSizeBytes)
            {
                reason =
                    $"target size changed for {action.TargetPath}: {action.TargetOriginalSizeBytes} -> {ti.Length}";
                return true;
            }

            if (action.TargetOriginalLastWriteTimeUtc.HasValue &&
                ti.LastWriteTimeUtc != action.TargetOriginalLastWriteTimeUtc.Value)
            {
                reason =
                    $"target last write time changed for {action.TargetPath}: " +
                    $"{action.TargetOriginalLastWriteTimeUtc.Value:O} -> {ti.LastWriteTimeUtc:O}";
                return true;
            }
        }

        // Canonical checks
        if (action.CanonicalOriginalSizeBytes > 0 || action.CanonicalOriginalLastWriteTimeUtc.HasValue)
        {
            var ci = new FileInfo(action.CanonicalPath);
            if (!ci.Exists)
            {
                reason = $"canonical missing since plan was created: {action.CanonicalPath}";
                return true;
            }

            if (action.CanonicalOriginalSizeBytes > 0 && ci.Length != action.CanonicalOriginalSizeBytes)
            {
                reason =
                    $"canonical size changed for {action.CanonicalPath}: {action.CanonicalOriginalSizeBytes} -> {ci.Length}";
                return true;
            }

            if (action.CanonicalOriginalLastWriteTimeUtc.HasValue &&
                ci.LastWriteTimeUtc != action.CanonicalOriginalLastWriteTimeUtc.Value)
            {
                reason =
                    $"canonical last write time changed for {action.CanonicalPath}: " +
                    $"{action.CanonicalOriginalLastWriteTimeUtc.Value:O} -> {ci.LastWriteTimeUtc:O}";
                return true;
            }
        }

        return false;
    }

    // ----------------- P/Invoke -----------------

    // Windows: CreateHardLinkW
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkWindows(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    // Unix: int link(const char *oldpath, const char *newpath);
    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LinkUnix(string oldpath, string newpath);
}
