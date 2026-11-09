#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Test runner lifecycle / output validation without a game or Wine process.
set -euo pipefail
src="$(cd "$(dirname "$0")" && pwd -P)"
test_root="$(mktemp -d -t kiln-runner-selftest-XXXXXXXX)"
trap 'rm -rf -- "$test_root"' EXIT
mkdir -p "$test_root/diagnostic" "$test_root/steam" "$test_root/library/src"
cp "$src/run-with-proton.sh" "$test_root/diagnostic/"
printf 'dummy Windows exe (not executed by fake Proton)' > "$test_root/diagnostic/eml-readonly-probe.exe"
printf '%s\n' '{"id":"flame-altar-expanded"}' > "$test_root/library/mod.json"
printf '%s\n' 'return true' > "$test_root/library/src/mod.lua"
fake="$test_root/fake-proton.sh"
cat > "$fake" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  waitforexitandrun)
    [[ "$#" == "5" ]]
    mkdir -p "$STEAM_COMPAT_DATA_PATH/pfx"
    echo "Proton: Upgrading prefix from None to 11.0-100"
    ;;
  runinprefix)
    [[ "$#" == "4" ]]
    echo "EML src/mod.lua: read OK (11 bytes)"
    echo "RESULT: PASS"
    echo "EML src/mod.lua: read OK (11 bytes)"
    echo "RESULT: PASS"
    ;;
  *) exit 4 ;;
esac
SH
chmod +x "$fake"
export TMPDIR="$test_root"
export STEAM_ROOT="$test_root/steam"
export KILN_EML_LIBRARY_DIR="$test_root/library"
export PROTON_RUNNER="$fake"
bash "$test_root/diagnostic/run-with-proton.sh" > "$test_root/success.log" 2>&1
grep -q 'Both read-only EML file checks completed.' "$test_root/success.log"

# A fake Steam launch can initialize the prefix and exit 0 without running
# the standalone probe. A subsequent runinprefix must produce actual results.
cat > "$fake" <<'SH'
#!/usr/bin/env bash
case "$1" in
  waitforexitandrun)
    mkdir -p "$STEAM_COMPAT_DATA_PATH/pfx"
    echo "Proton: Upgrading prefix from None to 11.0-100"
    exit 0 ;;
  runinprefix)
    echo "silent exit without executing a probe"
    exit 0 ;;
  *) exit 4 ;;
esac
SH
set +e
bash "$test_root/diagnostic/run-with-proton.sh" > "$test_root/missing-probe.log" 2>&1
result=$?
set -e
[[ "$result" == "3" ]] || { cat "$test_root/missing-probe.log"; echo "missing result must fail with code 3"; exit 1; }
grep -q 'the EML probe did not complete both file checks' "$test_root/missing-probe.log"
echo "Runner self-test PASSED: real read output required after direct runinprefix"
