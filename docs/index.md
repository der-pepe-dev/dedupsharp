# DedupSharp

Fast duplicate detector for files and media

Repository: `https://github.com/me/DedupSharp`

## Main goals

- Fast, correct duplicate detection for files and media.
- A reusable, frontend-agnostic core with optional CLI/GUI/plugin frontends.
- Speed (minimal I/O, smart pre-scans, SIMD where it helps) without sacrificing determinism.
- Extensible: exact binary core today; perceptual media and audio cores later.

## How agents should use this memory

- Start with this file, [[current-status]], [[instructions/agent-rules]], and [[tasks/lessons]].
- Use [[context-map]] to pick only the relevant docs for the task.
- Check [[environment]] before suggesting shell commands.
- Create one file per active task under `tasks/` (parallel tasks supported).
- Use [[tasks/todo]] as the durable backlog only.

## Instructions

- [[instructions/agent-rules]]
- [[instructions/cli-tooling]]
- [[context-map]]

## Task tracking

- [[tasks/todo]] — durable backlog by priority
- `tasks/<task-name>.md` — one file per active task
- `tasks/done/` — completed task files
- [[tasks/lessons]] — correction patterns and recurring mistakes
- [[tasks/task-template]] — reusable task note template

## Main documents

- [[current-status]]
- [[environment]]
- [[architecture]] — core/engine/frontend layering and core concepts
- [[roadmap]] — high-level direction
- [[performance]] — design techniques and benchmark scope
- README at repo root covers getting-started / build / contributing
