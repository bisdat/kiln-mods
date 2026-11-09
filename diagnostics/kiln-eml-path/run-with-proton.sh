#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Safe diagnostic: reads two existing Library file paths; DOES NOT launch
# Enshrouded, enable the mod, or alter any game/Kiln files.
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
probe="$script_dir/eml-readonly-probe.exe"
library="${KILN_EML_LIBRARY_DIR:-$HOME/.local/share/kiln/library/eml/flame-altar-expanded/0.1.1+4c68d588}"
steam_root="${STEAM_ROOT:-$HOME/.local/share/Steam}"

if [[ ! -f "$probe" ]]; then
  echo "Missing $probe: extract the downloaded diagnostic ZIP in one folder." >&2
  exit 2
fi
if [[ ! -r "$library/src/mod.lua" || ! -r "$library/mod.json" ]]; then
  echo "Missing/unreadable Library-only mod files: $library" >&2
  echo "Do NOT enable the mod. Inspect the Library import first." >&2
  exit 2
fi
if [[ ! -d "$steam_root" ]]; then
  echo "Cannot find Steam root: $steam_root; override STEAM_ROOT." >&2
  exit 2
fi

runner="${PROTON_RUNNER:-}"
if [[ -z "$runner" ]]; then
  mapfile -t candidates < <(find "$steam_root/steamapps/common" -mindepth 2 -maxdepth 2 -type f -name proton -print 2>/dev/null | sort)
  if (( ${#candidates[@]} == 1 )); then
    runner="${candidates[0]}"
  else
    echo "Select the Proton installation used by Enshrouded, for example:" >&2
    printf '  %s\n' "${candidates[@]}" >&2
    echo 'Then run: PROTON_RUNNER="/path/to/Proton .../proton" bash run-with-proton.sh' >&2
    exit 2
  fi
fi
if [[ ! -f "$runner" ]]; then
  echo "Proton runner not found: $runner" >&2
  exit 2
fi

# Translate both names for the SAME existing file. A difference between
# these reads can implicate the logical /home -> /var/home symlink mapping.
logical="$(python3 - "$library" <<'PY'
import os, sys
print("Z:" + os.path.abspath(sys.argv[1]).replace("/", chr(92)))
PY
)"
physical="$(python3 - "$library" <<'PY'
import os, sys
print("Z:" + os.path.realpath(sys.argv[1]).replace("/", chr(92)))
PY
)"

# Isolated Proton prefix in temporary storage. The probe is read-only, but
# Proton may create registry/prefix files: NEVER use the game's live prefix.
workspace="$(mktemp -d -t kiln-eml-readonly-XXXXXXXX)"
# Do NOT delete the prefix in EXIT: Proton's wineserver may still be writing
# registry files when the launcher returns. That race corrupted the first
# diagnostic attempt before the read-only executable started.
# Print the one-off location so it can be removed later when Wine is idle.
mkdir -p "$workspace/compatdata"
export STEAM_COMPAT_DATA_PATH="$workspace/compatdata"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="$steam_root"
export SteamAppId=1203620 SteamGameId=1203620
export STEAM_COMPAT_APP_ID=1203620

echo "EML read-only diagnostic runner v2 (Proton lifecycle fix)"
echo "Proton runner: $runner"
echo "Game is NOT launched. No Kiln profile or mod files are changed."
echo "The two tests refer to the same installed, Library-only file."
echo "Temporary diagnostic prefix (not auto-deleted): $workspace"
echo

# Use the same verb Steam uses for first-process startup. Capture output,
# distinguish an actual probe result from a successful Proton launcher exit,
# and keep the temporary prefix intact until Wine has finished.
set +e
"$runner" waitforexitandrun "$probe" "$logical" "$physical" 2>&1 | tee "$workspace/probe-output.log"
runner_status=${PIPESTATUS[0]}
set -e

# Two paths are supplied: the output must contain exactly two EML read
# outcomes and two RESULT lines. A prefix-only message does not count.
seen_reads="$(grep -c '^EML src/mod.lua:' "$workspace/probe-output.log" || true)"
seen_results="$(grep -c '^RESULT:' "$workspace/probe-output.log" || true)"
if (( seen_reads != 2 || seen_results != 2 )); then
  echo "ERROR: the EML probe did not complete both file checks." >&2
  echo "Proton exit status: $runner_status; read outcomes: $seen_reads; results: $seen_results" >&2
  echo "Diagnostic log: $workspace/probe-output.log" >&2
  echo "Retained temporary prefix: $workspace" >&2
  exit 3
fi
if (( runner_status != 0 )); then
  echo "EML probe completed with a failure (Proton exit status $runner_status)." >&2
  echo "Diagnostic log: $workspace/probe-output.log" >&2
  exit "$runner_status"
fi

echo
echo "Both read-only EML file checks completed."
echo "Diagnostic log: $workspace/probe-output.log"
echo "Temporary prefix retained to avoid deleting it while Wine writes its registry."
