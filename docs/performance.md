# DedupSharp performance notes

Speed is a primary design goal. Document tuning findings here as they emerge.

## Current techniques
- Size-first grouping to avoid unnecessary comparisons.
- Binary-compare fast path for pairs (early-out on first mismatch).
- `UsePreScan` flag to choose between:
  - two-pass `size → count → candidates` mode for large trees / slow disks,
  - single-pass `size → list` mode for fast SSD/NVMe or smaller trees.

## What the benchmark project measures (`DedupSharp.Benchmarks`)
- Binary compare variants (naive vs SIMD/AVX2)
- Buffer sizes
- Hash strategies (full vs partial)
- `UsePreScan` on/off
- Future parallelism strategies

## Tuning notes
<!-- Append dated benchmark findings here as the project evolves. -->
