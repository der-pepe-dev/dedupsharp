# GitHub Copilot instructions for DedupSharp

These instructions apply when assisting with DedupSharp.

Copilot does not follow file references, so the essentials are inlined here. The
fuller knowledge base lives in `docs/` (read by Claude Code and Codex).

## Project

- DedupSharp: Fast duplicate detector for files and media
- Stack: C#/.NET
- Environment: WSL2

## Repository layout

All source under `src/` (solution: `src/DedupSharp.slnx`):
- `DedupSharp.Core` — common types and abstractions (`IDuplicateScanner`, `ScanOptions`,
  `DuplicateGroup`, `FileEntry`, action planner/applier types). No I/O strategy here.
- `DedupSharp.Core.Exact` — exact binary duplicate engine (size grouping + compare/hash).
- `DedupSharp.Cli` — command-line frontend.
- `DedupSharp.Tests` — xUnit correctness tests.
- (planned) `DedupSharp.Benchmarks` — BenchmarkDotNet performance benchmarks.

## Engineering principles

- Keep code practical, simple, explicit, and debuggable.
- Concrete code over speculative framework work.
- Minimal-impact edits: touch only what is necessary.
- Root-cause fixes over temporary hacks.

## Coding conventions

- Public types are documented with XML doc comments; keep them.
- Options as `sealed class` with sensible defaults (see `ScanOptions`).
- Case-insensitive sets (`StringComparer.OrdinalIgnoreCase`) for extensions / ignored names.
- Scanners return `IEnumerable<DuplicateGroup>` and stay parallel-friendly per size group.
- Comparison strategy by group size: 1 → ignore, 2 → direct binary compare, >2 → hash group.
- Prefer SIMD / I/O-minimizing approaches where they measurably help; keep results deterministic.

## Do not

- Do not introduce broad speculative abstractions.
- Do not edit generated folders (`bin/`, `obj/`, build output).

- Do not put I/O or scan strategy in `DedupSharp.Core` — it stays abstraction-only.
- Do not make duplicate detection non-deterministic for the sake of speed.
- Do not bake frontend concerns (CLI parsing, GUI) into core/engine projects.
