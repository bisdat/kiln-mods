#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# READ-ONLY SNAPSHOT. Run once after a controlled startup attempt,
# before doing anything else with Kiln or launching Enshrouded again.
set -euo pipefail

game="${ENSHROUDED_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Enshrouded}"
capture_root="${EML_CAPTURE_DIR:-$HOME/Downloads}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
dest="$capture_root/kiln-eml-startup-$stamp-$$"
mkdir -m 700 -- "$dest"
for item in kiln/logs/eml.log kiln/state/eml.json kiln/state/boot.json; do
  src="$game/$item"
  if [[ -f "$src" ]]; then
    cp -p -- "$src" "$dest/$(basename "$item")"
  else
    printf 'MISSING %s\n' "$src" >> "$dest/MISSING.txt"
  fi
done
{
  printf 'Capture UTC: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf 'This is a read-only snapshot; file mtimes are preserved by cp -p.\n'
  printf 'Capture the first mod-enabled attempt before any subsequent boot.\n'
  printf 'File modification times (timestamps are evidence, not necessarily same process):\n'
  find "$dest" -maxdepth 1 -type f -printf '%TY-%Tm-%TdT%TH:%TM:%TS %s %f\n' | sort
} > "$dest/CAPTURE.txt"
printf 'Snapshot saved to: %s\n' "$dest"
cat "$dest/CAPTURE.txt"
