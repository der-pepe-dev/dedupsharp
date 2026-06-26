# Agent rules

Rules that apply to all agents (Claude Code, Codex, Copilot) working in this repo.

## Inspect before editing

- Read relevant project memory and source before proposing changes.
- Prefer the narrowest change that solves the problem.

## Verification is mandatory

- Never mark work complete without evidence (build/test/log/before-after).
- If verification cannot be run, state why and what remains unverified.

## Memory discipline

- Keep durable knowledge in `docs/`, not in local user-home memory.
- One file per active task under `tasks/<task-name>.md`.
- Update `current-status.md` and `index.md` only on durable changes.
- After a user correction, append a lesson to `tasks/lessons.md`.

## Source hygiene

- Work only in live source.
- Treat `bin/`, `obj/`, `artifacts/`, `.vs/`, build output, and nested
  extracts/snapshots as generated noise unless explicitly documented otherwise.

## Shell commands

- Check `docs/environment.md` for installed tools before assuming availability.
- Prefer the fast CLI tools listed in `instructions/cli-tooling.md` in pipelines.

<!-- TODO: add project-specific agent rules here -->
