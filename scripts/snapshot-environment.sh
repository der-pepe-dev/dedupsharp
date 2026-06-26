#!/usr/bin/env bash
# Snapshot the local dev environment into docs/environment.md (durable project memory).
# Run once per machine / account so distributed agents know what is actually installed
# and reachable, instead of guessing tool paths or RIDs.
#
# Usage: scripts/snapshot-environment.sh
set -euo pipefail

cd "$(dirname "$0")/.."
OUT="docs/environment.md"

emit() { printf '%s\n' "$1" >>"$OUT"; }

# version <command> <version-args...> -> "- `cmd` — first line of version output" if present
version() {
  local cmd="$1"; shift
  if command -v "$cmd" &>/dev/null; then
    local v
    v="$("$cmd" "$@" 2>/dev/null | tr -d '\000\r' | head -1 || true)"
    emit "- \`$cmd\` — ${v:-installed}"
  fi
}

mkdir -p "$(dirname "$OUT")"
: >"$OUT"
emit "# Environment snapshot"
emit ""
emit "_Generated: $(date '+%Y-%m-%d %H:%M') on \`$(uname -n)\` — regenerate with \`scripts/snapshot-environment.sh\`._"
emit ""
emit "> Per-machine. Development is distributed; values below reflect the host that"
emit "> generated this file. Agents: prefer tools listed here; if one is missing, suggest"
emit "> the documented install, not a Windows installer."
emit ""

emit "## WSL host"
emit "- Distro: $(lsb_release -ds 2>/dev/null || echo unknown)"
emit "- Kernel: $(uname -r)"
if command -v wsl.exe &>/dev/null; then
  wsl_ver="$(wsl.exe --version 2>/dev/null | tr -d '\000\r' | head -1)"
  emit "- WSL: ${wsl_ver:-interop available}"
fi
emit ""

emit "## .NET"
version dotnet --version
emit "- SDKs:"
dotnet --list-sdks 2>/dev/null | sed 's/^/    - /' >>"$OUT" || true
emit "- Runtimes:"
dotnet --list-runtimes 2>/dev/null | awk '{print $1, $2}' | sort -u | sed 's/^/    - /' >>"$OUT" || true
emit ""
emit "### .NET global tools"
dotnet tool list -g 2>/dev/null | tail -n +3 | awk 'NF{print "- `" $1 "` " $2}' >>"$OUT" || emit "- (none)"
emit ""
emit "_Diagnostics worth having (install via \`dotnet tool install -g\`): \`dotnet-trace\`,_"
emit "_\`dotnet-counters\`, \`dotnet-dump\` for frame-loop/hotpath profiling; \`csharpier\` for_"
emit "_deterministic formatting across agents. \`dotnet format\` ships with the SDK._"
emit ""

emit "## General CLI (referenced by CLAUDE.md / AGENTS.md)"
version git --version
version gh --version
version rg --version
version fd --version
version jq --version
version yq --version
version delta --version
version hyperfine --version
version csharpier --version
emit ""

emit "## Binary / data inspection"
version file --version
version xxd -v
version hexdump --version
version sqlite3 --version
emit ""

emit "## C++ / native build (Linux host)"
version cmake --version
version ninja --version
version gcc --version
version g++ --version
version gdb --version
emit ""

emit "## Native binary inspection (verify cross-compiled outputs)"
emit "Used to confirm a build produced a valid target binary, e.g. \`file x.dll\` ->"
emit "\"PE32+ executable (DLL) x86-64\", or \`nm -D\` / \`objdump -p\` to check exported symbols."
emit ""
version objdump --version
version nm --version
version readelf --version
# mingw-targeted inspection variants (read PE/COFF exports without a Windows host)
for c in x86_64-w64-mingw32-objdump x86_64-w64-mingw32-nm; do
  command -v "$c" &>/dev/null && emit "- \`$c\` — $("$c" --version 2>/dev/null | head -1)"
done
emit ""

emit "## mingw-w64 cross-compile (Windows DLLs from Linux)"
version x86_64-w64-mingw32-gcc --version
version x86_64-w64-mingw32-g++ --version
if command -v x86_64-w64-mingw32-gcc &>/dev/null; then
  emit "- sysroot: \`$(x86_64-w64-mingw32-gcc -print-sysroot 2>/dev/null || echo n/a)\`"
fi
emit ""

emit "## Windows interop (.exe, cross-compile edge cases only)"
emit "Reachable Windows executables on PATH via WSL interop (used only where no WSL"
emit "equivalent exists or output must target Windows specifically):"
emit ""
found_win=0
for c in MSBuild.exe signtool.exe dotnet.exe; do
  if command -v "$c" &>/dev/null; then
    emit "- \`$c\` — $(command -v "$c")"
    found_win=1
  fi
done
[ "$found_win" -eq 0 ] && emit "- (none on PATH)"
emit ""

emit "## Caveats for agents"
emit "- **Cross-host publish:** \`dotnet publish -r win-x64\` from this WSL host does **not**"
emit "  bundle the native DLLs by default — the \`.csproj\` copy gates are \`OS == Windows_NT\`"
emit "  only. Building the win-x64 RID on Linux additionally needs those gates relaxed to fire"
emit "  on a \`win\` RID. Don't assume a Linux-hosted Windows publish is self-contained."
emit "  See \`docs/cross-compile-native.md\`."
emit "- **Native build outputs** under \`src/**/Native/bin/\` and \`src/**/Native/build/\` are"
emit "  generated (recreated by the \`BuildNative*\` csproj targets on a Windows build) — never commit."
emit ""
