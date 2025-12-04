# DedupSharp

> **DedupSharp – Fast duplicate detector for files and media**

DedupSharp is a high-performance duplicate detector written in C#.  
It’s designed as a **reusable core library** with optional frontends (CLI, GUI, plugins), with a strong focus on:

- **Speed** – minimise disk I/O, use smart pre-scans, and lean on SIMD where it helps.
- **Correctness** – deterministic results with a clear model of what “duplicate” means.
- **Extensibility** – exact binary core today, media/audio cores later.

---

## Status

🔧 **Early development.**

Right now the focus is on:

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
- **Configurable pre-scan**:
  - Optional `UsePreScan` flag:
    - When enabled, a fast pre-scan builds a `size → count` map and only keeps sizes with `count > 1`.
    - When disabled, a single pass groups directly into `size → List<FileEntry>`.
- Smart comparison strategy:
  - If a size group has:
    - `1` file → ignored.
    - `2` files → direct binary comparison (fast early-out).
    - `> 2` files → hash-based grouping (currently SHA-256, with fast hashes planned).
- Designed to be parallel-friendly (per size group) and I/O-efficient.

### Media / audio (future cores)

These are **planned**, not implemented yet:

- **Media core** – perceptual comparison for images (and later video):
  - Perceptual hashes to detect images that are visually similar (resized, recompressed, small edits).
- **Audio core** – duplicate audio based on PCM and spectrograms:
  - Lossless: exact comparison on decoded PCM (container/metadata agnostic).
  - Lossy: generate spectrograms and reuse the same visual/perceptual hashing engine as the image core.

---

## Project structure

Planned / emerging layout:

```text
DedupSharp/
  DedupSharp.Core/          # Shared models and abstractions
  DedupSharp.Core.Exact/    # Exact binary duplicate engine
  DedupSharp.Tests/         # xUnit tests
  DedupSharp.Benchmarks/    # BenchmarkDotNet benchmarks
  DedupSharp.Cli/           # (future) CLI frontend
  DedupSharp.WinForms/      # (future) Windows GUI
