# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

- [ ] After org rename `der-pepe-dev` -> `der-pepe`, update git remote:
      `git remote set-url origin https://github.com/der-pepe/dedupsharp.git`
      (added 2026-06-26; `origin` currently points at der-pepe-dev/dedupsharp)

## Medium priority

- [ ] Cut the redundant `FileStream` buffer in `FilesAreEqualBinary` (and
      `ComputeHash`). Each compare opens two `FileStream`s with `bufferSize = 1 MB`,
      allocating a 1 MB internal buffer each — but we already do our own 1 MB pooled
      buffering. Verify-heavy scans still allocate ~308 MB from this. Pass a tiny
      `bufferSize` (e.g. 4096 or 1 to bypass) since reads go through our own buffer.

## Low priority / someday
