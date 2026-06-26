# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

- [ ] After org rename `der-pepe-dev` -> `der-pepe`, update git remote:
      `git remote set-url origin https://github.com/der-pepe/dedupsharp.git`
      (added 2026-06-26; `origin` currently points at der-pepe-dev/dedupsharp)

## Medium priority

- [ ] Add BLAKE3 as a `HashAlgorithmKind` (follow-up to feat/faster-hashing / PR #3).
      BLAKE3 is cryptographic + SIMD-fast, so it can be `IsCryptographic => true`
      (trusted for grouping, no mandatory binary-verify) — potentially the best
      default. Needs a native-lib NuGet (e.g. `Blake3` by xoofx); weigh the native
      dependency against the project's "simple, portable core" principle.

## Low priority / someday
