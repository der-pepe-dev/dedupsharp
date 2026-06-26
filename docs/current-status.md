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
- 2026-06-26: Added `DedupSharp.Benchmarks` (BenchmarkDotNet) with an
  `ExactScanMode` comparison benchmark; added GitHub Actions CI
  (`.github/workflows/ci.yml`) that restores, Release-builds, and tests on push/PR.
<!-- - YYYY-MM-DD: ... -->
