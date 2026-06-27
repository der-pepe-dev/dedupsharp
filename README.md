# DedupSharp

> DedupSharp – Fast duplicate detector for files and media

DedupSharp is a high-performance duplicate detector written in C#.
It’s designed as a reusable core library with optional frontends (CLI, GUI, plugins), with a strong focus on:

- Speed – minimise disk I/O, use smart pre-scans, and lean on SIMD where it helps.
- Correctness – deterministic results with a clear model of what “duplicate” means.
- Extensibility – exact binary core today, media/audio cores later.

---

## Status

🔧 Early development.

Current focus:

- `DedupSharp.Core` – common types and abstractions  
- `DedupSharp.Core.Exact` – exact binary duplicate engine (size + compare/hash)  
- `DedupSharp.Tests` – xUnit tests for correctness  
- `DedupSharp.Benchmarks` – BenchmarkDotNet benchmarks for performance  

CLI and GUI frontends will come later.

---

## Features (planned / in progress)

### Exact (binary) duplicate detection

- Size-based grouping:
  - Files are grouped by byte size first to avoid unnecessary work.
- Configurable pre-scan:
  - Optional `UsePreScan` flag:
    - When enabled, a fast pre-scan builds a `size → count` map and only keeps sizes with `count > 1`.
    - When disabled, a single pass groups directly into `size → List<FileEntry>`.
- Smart comparison strategy:
  - If a size group has:
    - 1 file → ignored.
    - 2 files → direct binary comparison (fast early-out).
    - > 2 files → hash-based grouping (currently SHA-256, with faster hashes planned).
- Designed to be parallel-friendly (per size group) and I/O-efficient.

### Media / audio (future cores)

Planned, not implemented yet:

- Media core – perceptual comparison for images (and later video):
  - Perceptual hashes to detect images that are visually similar (resized, recompressed, small edits).
- Audio core – duplicate audio based on PCM and spectrograms:
  - Lossless: exact comparison on decoded PCM (container/metadata agnostic).
  - Lossy: generate spectrograms and reuse the same visual/perceptual hashing engine as the image core.

---

## Project structure

Planned / emerging layout:

    DedupSharp/
      DedupSharp.Core/          # Shared models and abstractions
      DedupSharp.Core.Exact/    # Exact binary duplicate engine
      DedupSharp.Tests/         # xUnit tests
      DedupSharp.Benchmarks/    # BenchmarkDotNet benchmarks
      DedupSharp.Cli/           # (future) CLI frontend
      DedupSharp.WinForms/      # (future) Windows GUI

### Core concepts

- `ScanOptions` – what to scan and how:
  - `Paths` (folders/files), `Recursive`, `UsePreScan`
  - `MinFileSizeBytes`, `SafeExtensions`
  - `MaxDegreeOfParallelism` (for later parallel tuning).
- `FileEntry` – a file and its basic metadata (size, path, optional `Tag` for core-specific info).
- `DuplicateGroup` – one logical group of duplicates:
  - `DuplicateKind` (e.g. `Exact`, `MediaImage`, `AudioExact`, …)
  - `SizeBytes`
  - `Files` (list of `FileEntry`)
- `IDuplicateScanner` – main abstraction implemented by each core:
  - `ExactDuplicateScanner` lives in `DedupSharp.Core.Exact`.

---

## Getting started (library)

### Requirements

- .NET 8.0 (or later)

### Build

    dotnet build

### Run tests

    dotnet test

### Using the exact core from code

Minimal example of using `ExactDuplicateScanner`:

    using DedupSharp.Core;
    using DedupSharp.Core.Exact;

    var scanner = new ExactDuplicateScanner();

    var options = new ScanOptions
    {
        Paths = new[] { @"D:\Media", @"E:\Downloads" },
        Recursive = true,
        UsePreScan = true,
        MinFileSizeBytes = 1,
        SafeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".jpg", ".png", ".mp3", ".flac"
        },
        ProgressInterval = 1000,
        Progress = p =>
        {
            if (p.IsPhaseCompleted)
                Console.WriteLine($"[{p.Phase}] Files={p.FilesScanned}, Bytes={p.BytesScanned}");
        }
    };

    foreach (var group in scanner.Scan(options))
    {
        Console.WriteLine($"[Exact duplicate group, size = {group.SizeBytes} bytes]");
        foreach (var f in group.Files)
        {
            Console.WriteLine($"  {f.Path}");
        }
    }

Once the CLI project exists, there will be a simple front-end on top of this API.

---

## Performance

Speed is a primary design goal. DedupSharp uses:

- Size-first grouping to avoid unnecessary comparisons.
- A binary comparison fast path for pairs (early-out on first mismatch).
- A `UsePreScan` flag to choose between:
  - Two-pass “size → count → candidates” mode for large trees and slow disks.
  - Single-pass “size → list of files” mode for fast SSD/NVMe or smaller trees.
- A dedicated benchmark project (`DedupSharp.Benchmarks`) to measure:
  - Binary compare variants (naive vs SIMD/AVX2),
  - Buffer sizes,
  - Hash strategies (full vs partial),
  - `UsePreScan` on/off,
  - Future parallelism strategies.

As the project evolves, benchmark results and tuning notes can be documented here.

---

## Roadmap (high level)

- Exact binary core (initial version)
- Basic tests and benchmark scaffolding
- Optimised binary comparison (SIMD / AVX2, tuned buffer sizes)
- Faster non-crypto hashing (e.g. XXH / BLAKE3) for large groups
- CLI frontend (`DedupSharp.Cli`)
- Media core for images (perceptual hashes, similarity detection)
- Audio core (PCM-exact + spectrogram-based perceptual matching)
- Avalonia-based cross-platform GUI frontend
- Total Commander plugin

---

## Contributing

Issues and PRs are welcome once the core stabilises a bit.

For performance optimisations:

- Please add/update benchmarks in `DedupSharp.Benchmarks`.
- Try to keep changes covered by tests in `DedupSharp.Tests`.

---

## License

MIT – see `LICENSE`.

> **Media module dependency note:** `DedupSharp.Core.Media` depends on
> [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) (3.1.x), which is
> licensed under the Six Labors Split License. It is free to use in this open-source
> (MIT) project, but a **closed-source/commercial** product that consumes
> `DedupSharp.Core.Media` may require a commercial ImageSharp license. The exact and CLI
> cores have no such dependency.
