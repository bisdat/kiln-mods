#!/usr/bin/env bash
# Build the exact Kiln SDK used by this repository, then compile and package Survival QoL.
set -euo pipefail

repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
upstream="$repo/.kiln-upstream"
export KILN_OUT="$repo/.kiln-mdk"
export KILN_SDK_VERSION="0.2.0-dev.local.20261107.62100"
kiln_sha="9b9adada835c5f0a68b36567f939dc645cc1ef94"

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
dotnet restore SurvivalQoL/SurvivalQoL.csproj
dotnet build SurvivalQoL/SurvivalQoL.csproj --configuration Release --no-restore
dotnet build SurvivalQoL/SurvivalQoL.csproj --configuration Release --no-restore -t:KilnPack

package="$repo/bin/Release/survival-qol-0.1.0.zip"
[[ -s "$package" ]] || { echo "KilnPack did not produce $package" >&2; exit 1; }
echo "Survival QoL package: $package"
