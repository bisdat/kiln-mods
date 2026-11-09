#!/usr/bin/env bash
# Build against the newest *published* Kiln dev release, never moving main.
# The GitHub Releases API identifies a versioned release, the exact git tag is
# fetched, and the SDK is packed with that exact version. API failures abort.
set -euo pipefail

repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mod="${1:-SurvivalQoL}"
case "$mod" in
  SurvivalQoL) mod_id="survival-qol" ;;
  EndlessChests) mod_id="endless-chests-kiln" ;;
  CrossBaseStorage) mod_id="cross-base-storage" ;;
  CraftPaymentTrace) mod_id="craft-payment-trace" ;;
  *) echo "Unknown mod: $mod" >&2; exit 2 ;;
esac
project="$repo/$mod/$mod.csproj"
upstream="$repo/.kiln-upstream"
export KILN_OUT="$repo/.kiln-mdk"

command -v git >/dev/null || { echo "git is required" >&2; exit 1; }
command -v dotnet >/dev/null || { echo ".NET SDK 10 is required" >&2; exit 1; }
command -v python3 >/dev/null || { echo "python3 is required" >&2; exit 1; }

# Required before dotnet restore: it updates global.json's single
# msbuild-sdks/Kiln.Sdk mapping. GITHUB_TOKEN raises the API rate limit.
# Optional KILN_RELEASE_TAG is for an intentionally reproducible older build.
python3 "$repo/scripts/resolve-kiln-release.py" --root "$repo"
# shellcheck source=/dev/null
source "$repo/.kiln-release.env"

if [[ ! -d "$upstream/.git" ]]; then
  mkdir -p "$upstream"
  git -C "$upstream" init -q
  git -C "$upstream" remote add origin https://github.com/frostdev-ops/kiln.git
fi

git -C "$upstream" fetch --depth=1 origin "refs/tags/$KILN_UPSTREAM_TAG"
kiln_sha="$(git -C "$upstream" rev-parse 'FETCH_HEAD^{commit}')"
[[ "$kiln_sha" =~ ^[0-9a-f]{40}$ ]] || { echo "Invalid upstream commit SHA" >&2; exit 1; }

# A published release can point to a tag, not to moving 'main'.
git -C "$upstream" -c advice.detachedHead=false checkout -q --detach "$kiln_sha"

# Ensure the actual git tag commit is the published release target when the
# GitHub release specifies an immutable SHA (some releases specify branches).
python3 - "$repo/.kiln-release.json" "$kiln_sha" <<'PY'
import json, re, sys
path, actual = sys.argv[1:]
with open(path, encoding="utf-8") as f:
    info = json.load(f)
target = info["release_target"]
if re.fullmatch(r"[0-9a-fA-F]{40}", target) and target.lower() != actual:
    raise SystemExit(f"Kiln release tag mismatch: published {target}, checked out {actual}")
info["checked_out_sha"] = actual
with open(path, "w", encoding="utf-8") as f:
    json.dump(info, f, indent=2)
    f.write("\n")
PY

echo "Packing Kiln SDK $KILN_SDK_VERSION ($KILN_UPSTREAM_TAG at $kiln_sha)"
bash "$upstream/managed/Kiln.Sdk/pack.sh"

cd "$repo"
dotnet restore "$project"
dotnet build "$project" --configuration Release --no-restore
dotnet build "$project" --configuration Release --no-restore -t:KilnPack

mod_version="$(awk -F'[<>]' '/<Version>/ { print $3; exit }' "$project")"
[[ -n "$mod_version" ]] || { echo "Cannot read mod version from csproj" >&2; exit 1; }
package="$repo/bin/Release/$mod_id-$mod_version.zip"
[[ -s "$package" ]] || { echo "KilnPack did not produce $package" >&2; exit 1; }

# Sidecar provenance: users can see exactly which SDK the package used.
mkdir -p "$repo/bin/Release"
cp "$repo/.kiln-release.json" "$repo/bin/Release/kiln-sdk-provenance.json"
echo "$mod package: $package"
echo "Kiln provenance: $repo/bin/Release/kiln-sdk-provenance.json"
