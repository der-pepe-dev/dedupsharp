# CLI tooling

Fast CLI tools preferred over slower POSIX equivalents in shell pipelines. Check
`environment.md` for which are actually installed on the current host.

- `rg` (ripgrep) — code/text search, instead of `grep -r`.
- `fd` — file finding, instead of `find`.
- `jq` — JSON querying (CLI output, config, lockfiles).
- `yq` — YAML query/validate (e.g. CI workflow files).
- `delta` — readable git diffs (`git -c core.pager=delta diff`).
- `hyperfine` — command benchmarking (before/after timing).

Use dedicated editor/search tools when available; reach for these in shell pipelines.

<!-- TODO: add project-specific tools (e.g. binary inspection, db CLIs) -->
