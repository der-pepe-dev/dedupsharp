# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

- [ ] After org rename `der-pepe-dev` -> `der-pepe`, update git remote:
      `git remote set-url origin https://github.com/der-pepe/dedupsharp.git`
      (added 2026-06-26; `origin` currently points at der-pepe-dev/dedupsharp)

## Medium priority

- [ ] Implement the media (perceptual image) core per ADR
      `docs/decisions/0001-media-image-perceptual-dedup.md`: `DedupSharp.Core.Media`
      (ImageSharp), `PerceptualHashKind` (aHash/dHash/pHash), Hamming-distance
      clustering, `DuplicateKind`, hardlink guard for non-Exact groups.

## Low priority / someday
