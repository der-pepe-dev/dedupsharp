# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

- [ ] After org rename `der-pepe-dev` -> `der-pepe`, update git remote:
      `git remote set-url origin https://github.com/der-pepe/dedupsharp.git`
      (added 2026-06-26; `origin` currently points at der-pepe-dev/dedupsharp)

## Medium priority

- [ ] Consider per-algorithm default Hamming thresholds (pHash needs more slack than
      aHash/dHash). For large libraries, replace O(n^2) clustering with a BK-tree.

## Low priority / someday
