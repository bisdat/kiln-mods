#!/usr/bin/env bash
# Build the exact Kiln SDK used by this repository, then compile and package Survival QoL.
set -euo pipefail

repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mod="${1:-SurvivalQoL}"
case "$mod" in
  SurvivalQoL) mod_id="survival-qol" ;;
  EndlessChests) mod_id="endless-chests-kiln" ;;
  CrossBaseStorage) mod_id="cross-base-storage" ;;
  *) echo "Unknown mod: $mod" >&2; exit 2 ;;
esac
project="$repo/$mod/$mod.csproj"
upstream="$repo/.kiln-upstream"
export KILN_OUT="$repo/.kiln-mdk"
export KILN_SDK_VERSION="${KILN_SDK_VERSION:-0.2.0-dev.local.20261107.62100}"
kiln_sha="${KILN_UPSTREAM_SHA:-9b9adada835c5f0a68b36567f939dc645cc1ef94}"

command -v git >/dev/null || { echo "git is required" >&2; exit 1; }
command -v dotnet >/dev/null || { echo ".NET SDK 10 is required" >&2; exit 1; }

if [[ ! -d "$upstream/.git" ]]; then
  git clone --depth=1 https://github.com/frostdev-ops/kiln.git "$upstream"
fi

# Do not build against a moving upstream branch.
if [[ "$(git -C "$upstream" rev-parse HEAD)" != "$kiln_sha" ]]; then
  git -C "$upstream" fetch --depth=1 origin "$kiln_sha"
  git -C "$upstream" -c advice.detachedHead=false checkout --detach "$kiln_sha"
fi

echo "Packing Kiln SDK $KILN_SDK_VERSION from $kiln_sha"
bash "$upstream/managed/Kiln.Sdk/pack.sh"

cd "$repo"
dotnet restore "$project"
dotnet build "$project" --configuration Release --no-restore
dotnet build "$project" --configuration Release --no-restore -t:KilnPack

# KilnPack derives the archive name from the project version. Keep this in sync
# automatically when a new version is released.
mod_version="$(awk -F'[<>]' '/<Version>/ { print $3; exit }' "$project")"
[[ -n "$mod_version" ]] || { echo "Cannot read mod version from csproj" >&2; exit 1; }
package="$repo/bin/Release/$mod_id-$mod_version.zip"
[[ -s "$package" ]] || { echo "KilnPack did not produce $package" >&2; exit 1; }
echo "$mod package: $package"
