# 0001 — Media (perceptual image) duplicate core

Status: Accepted (2026-06-27). Design only; implementation is a gated follow-up.

## Context

The exact engine (`DedupSharp.Core.Exact`) finds byte-identical files. The roadmap's
next core is media: detecting **near-duplicate images** — the same picture resized,
recompressed, or lightly edited — which are not byte-identical and so are invisible to
the exact engine. This ADR records how that core is structured before any engine code is
written.

The existing pieces it builds on:
- `IDuplicateScanner.Scan(ScanOptions) -> IEnumerable<DuplicateGroup>` (`src/DedupSharp.Core/IDuplicateScanner.cs`).
- The `HashAlgorithmKind` pattern (`src/DedupSharp.Core/HashAlgorithmKind.cs`) — a
  JSON-string enum selecting an algorithm — which we reuse for perceptual hashes.
- The action layer (`DuplicateActionPlanner` / `DuplicateActionApplier`) which operates
  on `DuplicateGroup` and picks the lexically smallest path as canonical.

## Decision

1. **New project `DedupSharp.Core.Media`** implementing `IDuplicateScanner`, mirroring
   `DedupSharp.Core.Exact`. `DedupSharp.Core` stays abstraction-only; the media engine
   owns its strategy.

2. **Image decoding via SixLabors.ImageSharp (4.0).** Pure-managed, cross-platform, no
   native dependency — clean on the Linux/Windows/macOS CI matrix. It is licensed under
   the **Six Labors Split License**: free for this MIT/OSS project. See Consequences for
   the downstream caveat.

3. **`PerceptualHashKind` enum** in `DedupSharp.Core` — `AHash`, `DHash`, `PHash`,
   default `DHash` — serialized as a JSON string like `HashAlgorithmKind`. All three are
   implemented and selectable. Each produces a 64-bit hash from: decode → grayscale →
   resize:
   - **aHash** — 8×8; bit = pixel ≥ mean.
   - **dHash** — 9×8; bit = pixel brighter than its right neighbour.
   - **pHash** — 32×32 → 2-D DCT → top-left 8×8 low-frequency block; bit = coeff ≥ median.

4. **Grouping is near-duplicate clustering by Hamming distance ≤ threshold**, not exact
   equality. MVP uses union-find over pairwise comparisons (O(n²)); a BK-tree is a future
   optimization for large libraries. Default threshold ≈ 10/64, configurable. Output is
   deterministic: files ordered by path, clusters stable, representative = lexically
   smallest path (matching the planner's canonical choice).

5. **Add a `DuplicateKind` enum** (`Exact`, `MediaImage`; `AudioExact` reserved) to
   `DedupSharp.Core` and a `Kind` property on `DuplicateGroup`. The exact engine sets
   `Exact`; the media engine sets `MediaImage`. This bumps the saved-plan JSON version.

6. **Options stay on a single contract.** Extend `ScanOptions` with media fields
   (`PerceptualHashKind`, `HammingThreshold`, image-extension defaults); the exact engine
   ignores them. `IDuplicateScanner.Scan(ScanOptions)` is unchanged.

## Consequences

- **Dependency + license.** ImageSharp is added to `DedupSharp.Core.Media`. The Split
  License is free here, but a downstream **closed-source** product consuming
  `DedupSharp.Core.Media` would need a commercial ImageSharp license. Document this in the
  root README.
- **`DuplicateKind` ripples** into `DuplicateGroup`, the exact engine (set `Exact`), and
  the plan JSON (version bump + back-compat read).
- **Safety — hardlink guard.** Hardlinking *non-identical* near-duplicates is destructive
  (it makes two visibly-different images the same bytes). `DuplicateActionApplier` /
  `DuplicateActionPlanner` MUST refuse `ReplaceWithHardLink` for any non-`Exact` group.
  Move-to-quarantine and delete remain valid; dry-run + review get extra emphasis for
  media groups.
- **Heuristic results.** Perceptual matching has false positives/negatives; the threshold
  is a user-facing tuning knob, and media scans should default to dry-run.
- **Scale.** O(n²) clustering is acceptable for the MVP; large image libraries will need
  the BK-tree follow-up.

## Scope

MVP: images only; aHash/dHash/pHash selectable; Hamming-threshold clustering; `ScanOptions`
media fields; `DuplicateKind`; cross-platform tests with synthetic resized/recompressed
images. Audio (PCM-exact + spectrogram) and video are separate, later cores.
