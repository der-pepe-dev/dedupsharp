# Task: faster hashing for large groups

Branch: `feat/faster-hashing`

## Goal
Add a non-cryptographic hash option (XxHash3 / XxHash128 via `System.IO.Hashing`) to
speed up grouping of duplicate candidates (groups > 2), while keeping correctness.

## Correctness rule
Non-crypto hashes can collide, so a non-crypto hash is only used to **bucket**
candidates; buckets are then split by binary content. SHA-256 keeps current behaviour
(trusted unless `HashWithBinaryVerification`).

## Checklist
- [x] CPM: add `System.IO.Hashing` 10.0.9; reference it from `DedupSharp.Core.Exact`.
- [x] `HashAlgorithmKind` enum (Sha256 default, XxHash3, XxHash128); JSON string enum.
- [x] `ScanOptions.HashAlgorithm` (default Sha256).
- [x] Generalize `ComputeSha256` -> `ComputeHash(path, algo, ct)`.
- [x] Grouping: bucket by chosen hash; verify (partition by binary content) when
      `mode == HashWithBinaryVerification` OR algorithm is non-crypto. Also fixes the
      latent "drop non-canonical matches" behaviour via `PartitionByBinaryContent`.
- [x] Tests: parametrize correctness over algorithm (6 new; 44 total pass).
- [x] Benchmark: add `HashAlgorithm` param.

## Verification (done)
- Build clean; `dotnet test --solution src/DedupSharp.slnx` -> 44 passed.
- Benchmark dry-run (1 iteration, directional only):
  - `HashWithBinaryVerification`: SHA256 257ms vs XxHash3 190ms (~26% faster) vs XxHash128 204ms.
  - `HashOnly`: SHA256 176ms is fastest (no verify); non-crypto forces a binary-verify pass.
- Takeaway: XxHash3 wins when a verify pass happens anyway. A real (non-Dry) run is
  needed for authoritative numbers before tuning defaults.
