#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Tiny test fixture only: no real Kiln, game installation, world, or mod access.
set -euo pipefail
source_dir="$(cd "$(dirname "$0")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/game/kiln/plugins" "$tmp/game/kiln/logs" "$tmp/game/kiln/state" "$tmp/diag" "$tmp/evidence"
cp "$source_dir/manage-crc-test.sh" "$tmp/diag/"
printf 'original-kiln-dll\n' > "$tmp/game/kiln/plugins/kiln-eml.dll"
printf 'test-crc-reuse-dll\n' > "$tmp/diag/kiln-eml.dll"
printf 'first-log\n' > "$tmp/game/kiln/logs/eml.log"
printf '{"phase":"test"}\n' > "$tmp/game/kiln/state/eml.json"
printf '{"pid":999}\n' > "$tmp/game/kiln/state/boot.json"
export ENSHROUDED_GAME_DIR="$tmp/game" EML_CAPTURE_DIR="$tmp/evidence"

bash "$tmp/diag/manage-crc-test.sh" install
cmp "$tmp/diag/kiln-eml.dll" "$tmp/game/kiln/plugins/kiln-eml.dll"
printf 'second-log\n' > "$tmp/game/kiln/logs/eml.log"
# Refuse to overwrite backup with the currently installed experimental DLL.
if bash "$tmp/diag/manage-crc-test.sh" install >/dev/null 2>&1; then
  echo "ERROR: second install overwrote backup" >&2; exit 1
fi
bash "$tmp/diag/manage-crc-test.sh" restore
cmp "$tmp/diag/kiln-eml.original-before-crc.dll" "$tmp/game/kiln/plugins/kiln-eml.dll"
cmp -s <(printf 'original-kiln-dll\n') "$tmp/game/kiln/plugins/kiln-eml.dll"
find "$tmp/evidence" -type f -name eml.log -exec grep -q '^second-log$' '{}' ';'
find "$tmp/evidence" -type f -name CAPTURE.txt | grep -q CAPTURE.txt
echo "PASS: installer, immutable backup, captured post-test log, and verified rollback"
