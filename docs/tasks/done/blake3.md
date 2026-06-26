# Task: BLAKE3 hash option (done)

Branch: `feat/blake3`. Follow-up to faster-hashing (PR #3).

## Goal
Add BLAKE3 as a `HashAlgorithmKind`. Unlike XxHash, BLAKE3 is cryptographic +
SIMD-fast, so it is **trusted** for grouping (`IsCryptographic => true`) — no mandatory
binary-verify — at a fraction of SHA-256's cost.

## Done
- `Blake3` 2.2.1 (CPM) referenced from `DedupSharp.Core.Exact` (native libs incl.
  `linux-x64`).
- `HashAlgorithmKind.Blake3`; `ComputeHash` Blake3 case (inline read loop — `Hasher` is
  a struct, can't be captured in the `AppendStream` lambda); `IsCryptographic` returns
  true for Blake3.
- Tests parametrized over Blake3 (46 pass). Benchmark param includes Blake3.
- Housekeeping: moved `faster-hashing.md` to `done/`, removed BLAKE3 backlog item.

## Verification
- Build clean; `dotnet test --solution src/DedupSharp.slnx` -> 46 passed (also confirms
  the BLAKE3 native lib loads on linux-x64 / WSL; CI confirms on ubuntu).
- Benchmark dry-run including Blake3 (directional numbers in the PR).
