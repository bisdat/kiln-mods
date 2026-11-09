#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# TEST-ONLY Kiln EML CRC reuse plugin installer / evidence capture / rollback.
# Only changes installed kiln-eml.dll; NEVER touches game assets or save files.
set -euo pipefail

game="${ENSHROUDED_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Enshrouded}"
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
test_dll="$here/kiln-eml.dll"
target="$game/kiln/plugins/kiln-eml.dll"
original="$here/kiln-eml.original-before-crc.dll"
output_dir="${EML_CAPTURE_DIR:-$HOME/Downloads}"

fail() { printf 'ERROR: %s\n' "$*" >&2; exit 2; }
require_closed() {
  if pgrep -if '[e]nshrouded\.exe' >/dev/null; then
    fail "Enshrouded is running. Exit the game (and Kiln) before changing DLLs."
  fi
}
verify_file() { [[ -s "$1" && ! -L "$1" ]] || fail "Missing/empty/symlink: $1"; }
capture() {
  local dir name src
  dir="$(mktemp -d -p "$output_dir" "kiln-eml-crc-test-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXXXX")"
  chmod 700 "$dir"
  for name in "kiln/logs/eml.log" "kiln/state/eml.json" "kiln/state/boot.json"; do
    src="$game/$name"
    if [[ -f "$src" ]]; then
      cp -p -- "$src" "$dir/$(basename "$name")" ||
        printf 'COPY-FAILED %s\n' "$src" >> "$dir/MISSING.txt"
    else
      printf 'MISSING %s\n' "$src" >> "$dir/MISSING.txt"
    fi
  done
  {
    printf 'Capture UTC: %s\n' "$(date -u +%FT%TZ)"
    printf 'Capture context: after controlled Enshrouded startup, BEFORE any subsequent boot.\n'
    printf 'Source file modification timestamps preserved by cp -p; pid in log/state is authoritative.\n'
    find "$dir" -maxdepth 1 -type f -printf '%TY-%Tm-%TdT%TH:%TM:%TS %s %f\n' | sort
  } > "$dir/CAPTURE.txt"
  echo "Captured startup evidence: $dir"
  cat "$dir/CAPTURE.txt"
}
case "${1:-}" in
  install)
    require_closed
    verify_file "$test_dll"
    verify_file "$target"
    if [[ -e "$original" ]]; then
      verify_file "$original"
      cmp -s "$target" "$original" ||
        fail "Current DLL differs from existing saved original; refusing to replace."
    else
      # Atomic first-time backup; do not overwrite backups on subsequent runs.
      tmp_backup="$here/.kiln-original-backup-$$"
      trap 'rm -f -- "$tmp_backup"' EXIT
      cp -p -- "$target" "$tmp_backup"
      ln -- "$tmp_backup" "$original" ||
        fail "Unable to preserve original backup; aborting installation."
      rm -f -- "$tmp_backup"
      trap - EXIT
      cmp -s "$target" "$original" ||
        fail "Original backup verification failed; refusing to install."
    fi
    install -m 644 -- "$test_dll" "$target"
    cmp -s "$test_dll" "$target" || fail "Diagnostic DLL did not verify after install."
    echo "TEST-ONLY CRC-reuse Kiln EML DLL installed and byte-verified."
    echo "Original backup: $original"
    sha256sum "$target"
    echo "After ONE controlled startup, quit Enshrouded. Run: bash '$here/$(basename "$0")' restore"
    ;;
  restore)
    require_closed
    verify_file "$original"
    # Always attempt log capture BEFORE rollback; still restore if capture fails.
    capture || echo "WARNING: evidence capture incomplete; check the log directory." >&2
    install -m 644 -- "$original" "$target"
    cmp -s "$original" "$target" || fail "Original DLL restore verification failed."
    echo "Original kiln-eml.dll RESTORED and byte-verified."
    sha256sum "$target"
    echo "Disable Flame Altar Expanded in Kiln if you enabled it for this test."
    ;;
  capture)
    capture
    ;;
  *)
    echo "Usage: bash '$0' install|restore|capture" >&2
    exit 2
    ;;
esac
