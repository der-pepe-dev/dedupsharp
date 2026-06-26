using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

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
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (options is null) throw new ArgumentNullException(nameof(options));

        int total = 0;
        int applied = 0;
        int dryRunApplied = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;

            try
            {
                switch (action.Kind)
                {
                    case DupActionKind.MoveToQuarantine:
                        ApplyMoveToQuarantine(action, options, ref applied, ref dryRunApplied, ref skipped, log);
                        break;

                    case DupActionKind.Delete:
                        ApplyDelete(action, options, ref applied, ref dryRunApplied, ref skipped, log);
                        break;

                    case DupActionKind.ReplaceWithHardLink:
                        ApplyHardLink(action, options, ref applied, ref dryRunApplied, ref skipped, log);
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
            DryRunApplied = dryRunApplied,
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
        ref int dryRunApplied,
        ref int skipped,
        Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(options.QuarantineDirectory))
        {
            skipped++;
            log?.Invoke($"SKIP  MoveToQuarantine (QuarantineDirectory not set): {action.TargetPath}");
            return;
        }

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
        var destPath = Path.Combine(quarantineDir, fileName);

        // Resolve unique destination path (same logic for real and dry-run so the log is accurate).
        if (!options.DryRun)
            Directory.CreateDirectory(quarantineDir);

        destPath = GetUniquePath(destPath);

        if (options.DryRun)
        {
            dryRunApplied++;
            log?.Invoke($"DRY   MoveToQuarantine: {targetPath} -> {destPath}");
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
        ref int dryRunApplied,
        ref int skipped,
        Action<string>? log)
    {
        if (!options.AllowDelete)
        {
            skipped++;
            log?.Invoke($"SKIP  Delete (AllowDelete=false): {action.TargetPath}");
            return;
        }

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
            dryRunApplied++;
            log?.Invoke($"DRY   Delete: {targetPath}");
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
        ref int dryRunApplied,
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
            dryRunApplied++;
            log?.Invoke($"DRY   HardLink: {target} -> {canonical}");
            return;
        }

        // SAFETY: move the existing target out of the way first.
        var backupPath = GetBackupPath(target);
        File.Move(target, backupPath);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!CreateHardLinkWindows(target, canonical, IntPtr.Zero))
                {
                    var err = Marshal.GetLastWin32Error();
                    RestoreBackupIfNeeded(backupPath, target);
                    throw new IOException($"CreateHardLink failed with Win32 error {err} for {target}");
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                int result = LinkUnix(canonical, target);
                if (result != 0)
                {
                    var err = Marshal.GetLastWin32Error();
                    RestoreBackupIfNeeded(backupPath, target);

                    if (err == 18) // EXDEV: cross-device link (common on ZFS datasets)
                        throw new IOException($"Hardlink across filesystems is not supported for {target}.");

                    throw new IOException($"link() failed with errno {err} for {target}");
                }
            }
            else
            {
                RestoreBackupIfNeeded(backupPath, target);
                throw new PlatformNotSupportedException("Hardlink creation is not supported on this platform.");
            }

            File.Delete(backupPath);
            applied++;
            log?.Invoke($"APPLY HardLink: {target} -> {canonical}");
        }
        catch
        {
            RestoreBackupIfNeeded(backupPath, target);
            throw;
        }
    }

    private static string GetBackupPath(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath)!;
        var name = Path.GetFileName(originalPath);
        var backup = Path.Combine(dir, name + ".__dupbackup");

        int counter = 1;
        while (File.Exists(backup))
        {
            backup = Path.Combine(dir, $"{name}.__dupbackup{counter}");
            counter++;
        }

        return backup;
    }

    private static void RestoreBackupIfNeeded(string backupPath, string originalPath)
    {
        try
        {
            if (File.Exists(backupPath) && !File.Exists(originalPath))
                File.Move(backupPath, originalPath);
        }
        catch
        {
            // Don't hide the original error if rollback fails.
        }
    }

    // ----------------- Drift detection -----------------

    /// <summary>
    /// Checks whether either canonical or target has changed since planning (size/mtime).
    /// Returns false when no snapshot fields are populated (drift check disabled for old plans).
    /// </summary>
    private static bool HasDrifted(DupAction action, out string reason)
    {
        reason = string.Empty;

        bool hasSnapshot =
            action.CanonicalOriginalSizeBytes > 0 ||
            action.CanonicalOriginalLastWriteTimeUtc.HasValue ||
            action.TargetOriginalSizeBytes > 0 ||
            action.TargetOriginalLastWriteTimeUtc.HasValue;

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
                reason = $"target size changed for {action.TargetPath}: {action.TargetOriginalSizeBytes} -> {ti.Length}";
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
                reason = $"canonical size changed for {action.CanonicalPath}: {action.CanonicalOriginalSizeBytes} -> {ci.Length}";
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLinkWindows(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LinkUnix(string oldpath, string newpath);
}
