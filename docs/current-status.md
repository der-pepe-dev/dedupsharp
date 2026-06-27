# DedupSharp — current status

_Update only when durable project status changes: major feature completed, known
limitation discovered, milestone changed, or durable architectural direction changed.
Prefer appending dated notes over rewriting._

## Status

Early development. Exact (binary) duplicate detection is the active core; CLI is emerging.
Media (perceptual image) and audio (PCM/spectrogram) cores are planned, not implemented.

## Known limitations

- Faster-than-SHA-256 hashing is planned but not yet in place.
- Media/audio cores are design-only at this stage.
- GUI and plugin frontends are future work.

## Recent notes

<!-- Append dated notes here, newest first: -->
- 2026-06-27: Added the media (perceptual image) core `DedupSharp.Core.Media`
  (`MediaImageScanner`, ImageSharp 3.1.x). Implements aHash/dHash/pHash (selectable via
  `PerceptualHashKind`), Hamming-distance union-find clustering, `DuplicateKind` on
  `DuplicateGroup`, and a planner guard refusing hardlink for non-`Exact` groups. Per
  ADR 0001. Note: ImageSharp v4 enforces a build-time license key, so we pin 3.1.x
  (Split License v1, free for OSS without a key). pHash needs a looser Hamming threshold
  than aHash/dHash on smooth images.
- 2026-06-27: Added BLAKE3 (`HashAlgorithmKind.Blake3`, `Blake3` NuGet 2.2.1). BLAKE3
  is cryptographic so it is trusted for grouping (`IsCryptographic => true`) — no
  mandatory binary-verify, unlike XxHash. Confirmed by allocation profile (~300 MB,
  like SHA-256, vs XxHash's ~900 MB verify pass). The Dry benchmark is I/O-bound on the
  slow mount, so it does not yet show a CPU speedup; a warm, CPU-bound run is needed for
  authoritative hash-throughput numbers before changing the default algorithm.
- 2026-06-26: Bug-fix pass. Fixed a data-loss path where overlapping scan inputs
  (dir+subdir, dir+file, or a repeated path) made a file its own "duplicate"
  (scanner now de-dupes by full path; planner skips `target == canonical`). Fixed
  Windows hardlink P/Invoke (`EntryPoint = "CreateHardLink"`). Made scan output
  deterministic (sorted groups/files). Replaced the 0-byte "no snapshot" sentinel
  with explicit `*SnapshotRecorded` flags so empty files keep drift protection.
  Plus: mid-file cancellation, narrowed enumeration catches, `--min-size` overflow
  guard, non-interactive apply guard, scan-only no longer writes a plan file.
  38 tests pass.
- 2026-06-26: Added `DedupSharp.Benchmarks` (BenchmarkDotNet) with an
  `ExactScanMode` comparison benchmark; added GitHub Actions CI
  (`.github/workflows/ci.yml`) that restores, Release-builds, and tests on push/PR.
<!-- - YYYY-MM-DD: ... -->
