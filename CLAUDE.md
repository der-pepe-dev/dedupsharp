# DedupSharp — Claude instructions

## Bootstrap

- Stack: C#/.NET
- Environment: WSL2
- Build: `dotnet build src/DedupSharp.slnx` | Test: `dotnet test src/DedupSharp.slnx`

## Startup

1. Read `docs/index.md`.
2. Read `docs/current-status.md`.
3. Read `docs/environment.md` before suggesting any shell command.
4. Read `docs/instructions/agent-rules.md`.
5. Review `docs/tasks/lessons.md` for relevant correction patterns.
6. For task-specific work, read only the docs linked from `docs/context-map.md`.

Do not load or rewrite the entire Obsidian folder unless explicitly asked.

## Project memory rule

Development may be distributed across machines and accounts. Keep project memory in
this repository, not in local user-home memory.

Use:

- `docs/` for human-readable persistent project memory.
- `docs/index.md` as the map of important memory files.
- `docs/current-status.md` for durable project status.
- `docs/context-map.md` for which memory files to read per task.
- `docs/environment.md` for installed tools (regenerate with `scripts/snapshot-environment.sh`).
- `docs/decisions/` for durable architectural decisions (ADRs).
- `docs/tasks/todo.md` for the durable backlog.
- `docs/tasks/<task-name>.md` for active task plans (one file per task; parallel tasks supported).
- `docs/tasks/done/` for completed task files.
- `docs/tasks/lessons.md` for correction patterns and recurring mistakes.
- `.claude/skills/` for repeatable Claude Code workflows.
- `.claude/agents/` for focused specialist reviewers/debuggers.

Do not modify `docs/.obsidian/` (the Obsidian app config folder, if you open `docs/` as an Obsidian vault). It is always named `.obsidian` regardless of the vault folder name.

## Task workflow

Multiple tasks can run in parallel. Each task gets its own file:
`docs/tasks/<task-name>.md`.

For non-trivial, risky, architectural, or multi-file work:

1. Inspect relevant project memory first.
2. Inspect relevant source files before proposing edits.
3. Create `docs/tasks/<task-name>.md` with a short checklist.
4. Implement in small, verifiable steps.
5. Verify with the narrowest useful build/test/run command.
6. Update the task file with results as work progresses.
7. When done, move the file to `docs/tasks/done/`.
8. Update durable Obsidian docs only if project knowledge changed.

For simple compile errors, focused bug fixes, typo fixes, small test fixes, or
user-explicit "just fix it" tasks:

- do not wait for plan approval,
- inspect first,
- make the smallest safe change,
- verify,
- summarize.

If something goes sideways, stop and re-plan. Do not keep pushing through uncertainty.

## Verification

Never mark work complete without evidence. Prefer: build result, test result, log
output, before/after behavior, exact files changed. If verification cannot be run,
say exactly why and what remains unverified.

## Engineering principles

- C#/.NET.
- Keep code practical and simple.
- Minimal impact: touch only what is necessary.
- Root-cause fixes over temporary hacks.
- Avoid broad abstractions unless they solve a real, recurring problem.

- Core library is frontend-agnostic: `DedupSharp.Core` holds types/abstractions only.
- Scanner implementations live behind `IDuplicateScanner` (e.g. `DedupSharp.Core.Exact`).
- Determinism over cleverness: a scan must give reproducible duplicate groups.
- I/O-efficiency is a primary goal: prefer size pre-scan, early-out binary compare for
  pairs, hash-based grouping (SHA-256 today) only for groups > 2.
- Keep frontends (CLI today; GUI/plugins later) thin over the core.

## Documentation updates

Update `docs/current-status.md` only when durable project status changes.
Update `docs/index.md` only when adding, removing, or renaming important docs.
Prefer appending dated notes or creating ADRs over rewriting large memory files.
After user corrections, append a lesson to `docs/tasks/lessons.md`.

## Claude Code usage pattern

Use skills for repeated procedures:

- `/start-task`
- `/finish-task`

(none yet — add as repeatable workflows emerge, e.g. a benchmark-compare skill)

Use project subagents for focused review/exploration when they reduce main-context noise.

## Source hygiene

Work only in live source. Treat `bin/`, `obj/`, `artifacts/`, `.vs/`, build output
folders, and any nested zip/extract/snapshot as generated noise unless explicitly
documented otherwise.

## CLI tooling

See `docs/instructions/cli-tooling.md` for the fast CLI tools preferred in shell
pipelines. Check `docs/environment.md` for what is actually installed on this host.
