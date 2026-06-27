# DedupSharp architecture

Distilled from the project README and source. Durable architecture/domain knowledge
for agents; build/getting-started prose stays in the repo-root README.

## Layering

A reusable, frontend-agnostic core with optional frontends layered on top.

- `DedupSharp.Core` — shared models and abstractions only. No I/O or scan strategy.
- `DedupSharp.Core.Exact` — exact binary duplicate engine (size grouping + compare/hash).
- `DedupSharp.Core.Media` — perceptual image engine (ImageSharp decode + aHash/dHash/pHash
  + Hamming-distance clustering). See [[decisions/0001-media-image-perceptual-dedup]].
- `DedupSharp.Tests` — xUnit correctness tests.
- `DedupSharp.Benchmarks` — BenchmarkDotNet performance benchmarks (scaffolding).
- `DedupSharp.Cli` — CLI frontend (emerging).
- `DedupSharp.WinForms` / GUI / plugins — future.

Keep frontends thin over the core. Avoid pushing scan strategy or I/O into `DedupSharp.Core`.

## Core concepts

- `ScanOptions` — what to scan and how: `Paths`, `Recursive`, `UsePreScan`,
  `MinFileSizeBytes`, `SafeExtensions`, `IgnoredDirectoryNames`, `IgnoredFileNames`,
  `ExactMode`, and progress controls. Sensible defaults; case-insensitive sets for
  extensions and ignored names.
- `FileEntry` — a file and its basic metadata (size, path, optional `Tag` for
  core-specific info).
- `DuplicateGroup` — one logical group of duplicates: `DuplicateKind`
  (e.g. `Exact`, `MediaImage`, `AudioExact`), `SizeBytes`, and `Files`.
- `IDuplicateScanner` — the abstraction each core implements. `Scan(ScanOptions)`
  returns `IEnumerable<DuplicateGroup>`. `ExactDuplicateScanner` lives in
  `DedupSharp.Core.Exact`.

## Exact engine strategy

1. Group files by byte size first (avoids unnecessary work).
2. Optional pre-scan (`UsePreScan`): a fast `size → count` pass keeps only sizes with
   `count > 1`; disabled, a single pass groups directly into `size → List<FileEntry>`.
3. Comparison by group size:
   - 1 file → ignored
   - 2 files → direct binary comparison (early-out on first mismatch)
   - > 2 files → hash-based grouping (SHA-256 today; faster hashes planned)
4. Parallel-friendly per size group; I/O-efficient by design.

## Design invariants

- Deterministic results — never trade determinism for speed.
- Core stays abstraction-only; engines own strategy; frontends own UX.
- Performance is a primary goal but must not compromise correctness.
