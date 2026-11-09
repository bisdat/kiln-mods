#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Scratch-only Windows metadata capability test on the actual Steam Proton/game disk.
# Does NOT launch the game, load mods, or alter game data / profiles / worlds.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
probe="$here/eml-stamp-probe.exe"
steam_root="${STEAM_ROOT:-$HOME/.local/share/Steam}"
game="${ENSHROUDED_GAME_DIR:-$steam_root/steamapps/common/Enshrouded}"
scratch="$game/kiln/eml/cache"
runner="${PROTON_RUNNER:-$steam_root/steamapps/common/Proton - Experimental/proton}"
[[ -f "$probe" ]] || { echo "Missing executable: $probe" >&2; exit 2; }
[[ -f "$runner" ]] || { echo "Missing Proton runner: $runner" >&2; exit 2; }
[[ -d "$scratch" ]] || { echo "Missing existing EML cache directory: $scratch" >&2; exit 2; }
if pgrep -if '[e]nshrouded\.exe' >/dev/null; then
  echo "Enshrouded is still running. Quit before probing." >&2
  exit 2
fi
if [[ -e "$scratch/eml-wine-stamp-scratch.tmp" ]]; then
  echo "Scratch probe filename already exists; refusing to overwrite it." >&2
  exit 2
fi
workspace="$(mktemp -d -t eml-stamp-proton-XXXXXXXX)"
mkdir -p "$workspace/compatdata"
export STEAM_COMPAT_DATA_PATH="$workspace/compatdata"
export STEAM_COMPAT_CLIENT_INSTALL_PATH="$steam_root"
export SteamAppId=1203620 SteamGameId=1203620 STEAM_COMPAT_APP_ID=1203620
echo "Test-only scratch probe on existing Enshrouded EML cache volume"
echo "Does not launch Enshrouded, change any mod, or read game data."
echo "Scratch prefix: $workspace"
set +e
"$runner" waitforexitandrun 'C:\\windows\\system32\\cmd.exe' /c exit   2>&1 | tee "$workspace/initialize.log"
init=${PIPESTATUS[0]}
set -e
if ((init != 0)) || [[ ! -d "$workspace/compatdata/pfx" ]]; then
  echo "ERROR: isolated Proton prefix failed; see $workspace/initialize.log" >&2
  exit 3
fi
echo "Checking native file identity and preserved-mtime ChangeTime under this Proton..."
set +e
(cd "$scratch" && "$runner" runinprefix "$probe") 2>&1 | tee "$workspace/stamp-result.log"
status=${PIPESTATUS[0]}
set -e
if ((status != 0)) || ! grep -q '^WINE_CHANGE_STAMP=' "$workspace/stamp-result.log"; then
  echo "ERROR: change-stamp probe did not complete (exit $status)" >&2
  echo "Log: $workspace/stamp-result.log" >&2
  exit 3
fi
echo "Result saved: $workspace/stamp-result.log"
echo "VERIFIED means the filesystem is eligible for automatic runtime validation."
echo "UNSUPPORTED or UNRELIABLE means the performance build must fall back to full CRC."
echo "Temporary Proton prefix retained to avoid Wine registry cleanup races."
