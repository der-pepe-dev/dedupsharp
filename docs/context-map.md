# DedupSharp context map

Use this file to decide which memory files to read for a task. Do not read every
file by default.

## Always read at session start

- [[index]]
- [[current-status]]
- [[environment]]
- [[instructions/agent-rules]]
- [[tasks/lessons]]

## General architecture

Read when touching cross-cutting structure, project boundaries, or core contracts.

- [[architecture]]
- [[current-status]]

## Build / tooling / environment

Read when touching build scripts, CI, packaging, or cross-compilation.

- [[environment]]
- [[instructions/cli-tooling]]

## Exact / binary engine

Read when touching size grouping, pre-scan, binary compare, or hashing strategy.

- [[architecture]]
- `src/DedupSharp.Core.Exact/` and `src/DedupSharp.Core/ScanOptions.cs`

## Core abstractions / API surface

Read when changing `IDuplicateScanner`, `ScanOptions`, `DuplicateGroup`, or the
action planner/applier contracts.

- [[architecture]]

## Performance / benchmarks

Read when optimizing hot paths or comparing before/after.

- [[performance]]
- [[instructions/cli-tooling]] (hyperfine)
- `DedupSharp.Benchmarks` (BenchmarkDotNet)
